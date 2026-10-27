using Akagi.Characters.Conversations;
using Akagi.Characters.Groups;
using Akagi.Life;
using Akagi.Users;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Akagi.Characters.Routines;

/// <summary>
/// One pass over all characters with routines: fires at most one routine per character and delivers the outreach.
/// </summary>
internal class RoutineRunner
{
    private readonly ICharacterDatabase _characters;
    private readonly IUserDatabase _users;
    private readonly IRoutineDatabase _routines;
    private readonly ILifeProfileDatabase _profiles;
    private readonly Func<Character, User, Message, Task> _deliver;
    private readonly ILogger _logger;

    public RoutineRunner(ICharacterDatabase characters,
                         IUserDatabase users,
                         IRoutineDatabase routines,
                         ILifeProfileDatabase profiles,
                         Func<Character, User, Message, Task> deliver,
                         ILogger logger)
    {
        _characters = characters;
        _users = users;
        _routines = routines;
        _profiles = profiles;
        _deliver = deliver;
        _logger = logger;
    }

    public async Task<int> TickAsync(DateTime nowUtc, CancellationToken cancellationToken = default)
    {
        int fired = 0;
        List<Character> characters = await _characters.GetCharactersWithRoutines();
        foreach (string ownerId in await _routines.GetOwnerCharacterIds())
        {
            if (characters.All(c => c.Id != ownerId) && await _characters.GetCharacter(ownerId) is Character owner)
            {
                characters.Add(owner);
            }
        }

        foreach (Character character in characters)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!character.AllowAutomaticProcessing)
            {
                continue;
            }

            try
            {
                if (await TickCharacterAsync(character, nowUtc))
                {
                    fired++;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Routine evaluation failed for character {CharacterId}", character.Id);
            }
        }
        return fired;
    }

    private async Task<bool> TickCharacterAsync(Character character, DateTime nowUtc)
    {
        User? user = await _users.GetDocumentByIdAsync(character.UserId);
        if (user == null)
        {
            return false;
        }

        LifeProfile profile = await _profiles.GetOrCreate(user.Id!);
        List<Routine> routines = await LoadRoutines(_routines, character);
        await PruneFinished(routines, nowUtc);
        (DateTime? lastUser, DateTime? lastCharacter) = RoutineEngine.LastMessageTimes(character);

        RoutineState state = new()
        {
            NowUtc = nowUtc,
            Profile = profile,
            LastUserMessageUtc = lastUser,
            LastCharacterMessageUtc = lastCharacter
        };

        (Routine Routine, RoutineDecision Decision)? selected = RoutineEngine.SelectDue(routines, state);
        if (selected == null)
        {
            return false;
        }

        (Routine routine, RoutineDecision decision) = selected.Value;
        _logger.LogInformation("Routine {Routine} fired for character {CharacterId}: {Reason}", routine.Name, character.Id, decision.Reason);

        // Persist before delivering so a failing LLM call does not make the routine fire again every tick
        routine.OnFired(state);
        await _routines.SaveAsync(routine);
        if (profile.Dirty)
        {
            await _profiles.SaveAsync(profile);
        }

        TextMessage message = new()
        {
            From = Message.Type.System,
            Text = RoutineEngine.BuildInstruction(routine, decision),
            Time = nowUtc,
            CharacterId = null
        };
        await _deliver(character, user, message);
        return true;
    }

    /// <summary>
    /// The routines from the character's graph plus the ones it scheduled for itself.
    /// </summary>
    public static async Task<List<Routine>> LoadRoutines(IRoutineDatabase database, Character character)
    {
        List<Routine> routines = await database.GetRoutines(character.RoutineIds);
        if (!string.IsNullOrEmpty(character.Id))
        {
            routines.AddRange((await database.GetOwnedRoutines(character.Id)).Where(r => routines.All(g => g.Id != r.Id)));
        }
        return routines;
    }

    /// <summary>
    /// Deletes self-made one-time check-ins that are done or missed for a week, so the list stays readable.
    /// </summary>
    private async Task PruneFinished(List<Routine> routines, DateTime nowUtc)
    {
        foreach (OneTimeRoutine routine in routines.OfType<OneTimeRoutine>().Where(r => r.CreatedBySelf).ToList())
        {
            DateTime? finished = routine.LastFiredUtc ?? (routine.IsExpired(nowUtc) ? routine.DueUtc : null);
            if (finished.HasValue && nowUtc - finished.Value > TimeSpan.FromDays(7) && routine.Id != null)
            {
                await _routines.DeleteDocumentByIdAsync(routine.Id);
                routines.Remove(routine);
            }
        }
    }
}

internal class RoutineService : BackgroundService
{
    public static readonly TimeSpan Interval = TimeSpan.FromMinutes(1);

    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<RoutineService> _logger;

    public RoutineService(IServiceProvider serviceProvider, ILogger<RoutineService> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        IProactiveDelivery delivery = _serviceProvider.GetRequiredService<IProactiveDelivery>();
        RoutineRunner runner = new(
            _serviceProvider.GetRequiredService<ICharacterDatabase>(),
            _serviceProvider.GetRequiredService<IUserDatabase>(),
            _serviceProvider.GetRequiredService<IRoutineDatabase>(),
            _serviceProvider.GetRequiredService<ILifeProfileDatabase>(),
            delivery.DeliverAsync,
            _logger);

        while (!stoppingToken.IsCancellationRequested)
        {
            await Task.Delay(Interval, stoppingToken);
            try
            {
                await runner.TickAsync(DateTime.UtcNow, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Routine tick failed");
            }
        }
    }
}

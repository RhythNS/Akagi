using Akagi.Characters;
using Akagi.Characters.CharacterBehaviors.Puppeteers;
using Akagi.Characters.CharacterBehaviors.Reflectors;
using Akagi.Characters.CharacterBehaviors.SystemProcessors;
using Akagi.Characters.Conversations;
using Akagi.Characters.TriggerPoints;
using Akagi.Communication;
using Akagi.Data;
using Akagi.Flow;
using Akagi.LLMs;
using Akagi.Scheduling.Tasks;
using Akagi.Users;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;

namespace Akagi.Receivers;

internal class Receiver : IReceiver, ICleanable
{
    private static readonly ConcurrentDictionary<(string userId, string characterId), SemaphoreSlim> _locks = new();

    private readonly IPuppeteerDatabase _puppeteerDatabase;
    private readonly IReflectorDatabase _reflectorDatabase;
    private readonly ISystemProcessorDatabase _systemProcessorDatabase;
    private readonly ILLMFactory _llmFactory;
    private readonly IDatabaseFactory _databaseFactory;
    private readonly ILogger<Receiver> _logger;

    public Receiver(IPuppeteerDatabase puppeteerDatabase,
                    IReflectorDatabase reflectorDatabase,
                    ISystemProcessorDatabase systemProcessorDatabase,
                    ILLMFactory llmFactory,
                    IDatabaseFactory databaseFactory,
                    ILogger<Receiver> logger)
    {
        _puppeteerDatabase = puppeteerDatabase;
        _reflectorDatabase = reflectorDatabase;
        _systemProcessorDatabase = systemProcessorDatabase;
        _llmFactory = llmFactory;
        _databaseFactory = databaseFactory;
        _logger = logger;
    }

    public Task CleanUpAsync()
    {
        CleanupUnusedLocks();
        return Task.CompletedTask;
    }

    public async Task Reflect(Character character, User user, string name)
    {
        LockCharacter(character, user);
        try
        {
            ICommunicator? communicator = Globals.Instance.ServiceProvider.GetRequiredService<ICommunicatorFactory>().Create(user.LastUsedCommunicator);
            if (communicator == null)
            {
                _logger.LogWarning("No communicator found for user {UserId}", user.Id);
                return;
            }

            await using Context context = GetContext(character, user, communicator);

            bool reflected = false;
            foreach (string id in character.ReflectorIds)
            {
                Reflector reflector = await _reflectorDatabase.GetDocumentByIdAsync(id)
                        ?? throw new Exception($"Reflector with ID {id} not found for character {character.Id}");

                if (string.Equals(reflector.Name, name, StringComparison.OrdinalIgnoreCase) == false)
                {
                    continue;
                }

                ILogger logger = Globals.Instance.ServiceProvider.GetRequiredService<ILogger<Reflector>>();

                await reflector.Init(logger, context, _systemProcessorDatabase);
                await reflector.ProcessAsync();

                reflected = true;
            }

            if (reflected)
            {
                await TriggerForCharacter(TriggerPoint.TriggerType.ReflectionCompleted, character);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error reflecting for user {UserId} and character {CharacterId}", user.Id, character.Id);
            return;
        }
        finally
        {
            ReleaseLock(character, user);
        }
    }

    public async Task OnSystemEvent(Character character, User user, Message message)
    {
        LockCharacter(character, user);

        try
        {
            ICommunicator? communicator = Globals.Instance.ServiceProvider.GetRequiredService<ICommunicatorFactory>().Create(user.LastUsedCommunicator);
            if (communicator == null)
            {
                _logger.LogWarning("No communicator found for user {UserId}", user.Id);
                return;
            }

            await using Context context = GetContext(character, user, communicator);
            context.Conversation.AddMessage(message);

            await ProcessCharacterMessageAsync(context);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing system event for user {UserId} and character {CharacterId}", user.Id, character.Id);
            return;
        }
        finally
        {
            ReleaseLock(character, user);
        }
    }

    public async Task OnMessageRecieved(ICommunicator from, User user, Character character, Message message)
    {
        if (!TryLockCharacter(character, user))
        {
            await from.SendMessage(user, character, "Character is busy processing another message. Please try again later.");
            return;
        }

        try
        {
            await using Context context = GetContext(character, user, from);

            context.Conversation.AddMessage(message);
            await ProcessCharacterMessageAsync(context);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing message for user {UserId} and character {CharacterId}", user.Id, character.Id);
            await from.SendMessage(user, character, "An error occurred while processing your message. Please try again later.");
        }
        finally
        {
            ReleaseLock(character, user);
        }
    }

    public async Task OnPromptContinue(ICommunicator? from, Character character, User user)
    {
        if (!TryLockCharacter(character, user))
        {
            return;
        }

        try
        {
            from ??= Globals.Instance.ServiceProvider.GetRequiredService<ICommunicatorFactory>().Create(user.LastUsedCommunicator);
            if (from == null)
            {
                _logger.LogWarning("No communicator found for user {UserId}", user.Id);
                return;
            }

            await using Context context = GetContext(character, user, from);

            await ProcessCharacterMessageAsync(context);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing message for user {UserId} and character {CharacterId}", user.Id, character.Id);
            if (from != null)
            {
                await from.SendMessage(user, character, "An error occurred while processing your message. Please try again later.");
            }
        }
        finally
        {
            ReleaseLock(character, user);
        }
    }

    private async Task ProcessCharacterMessageAsync(Context context)
    {
        Puppeteer puppeteer = await _puppeteerDatabase.GetDocumentByIdAsync(context.Character.PuppeteerId)
                    ?? throw new Exception($"Puppeteer with ID {context.Character.PuppeteerId} not found for character {context.Character.Id}");

        ILogger logger = Globals.Instance.ServiceProvider.GetRequiredService<ILogger<Puppeteer>>();

        await puppeteer.Init(logger, context, _systemProcessorDatabase);
        await puppeteer.ProcessAsync();

        await TriggerForCharacter(TriggerPoint.TriggerType.MessageProcessed, context.Character);
    }

    private Context GetContext(Character character, User user, ICommunicator communicator)
    {
        return new Context()
        {
            Character = character,
            Conversation = character.GetCurrentConversation(),
            User = user,
            Communicator = communicator,
            LLMFactory = _llmFactory,
            DatabaseFactory = _databaseFactory
        };
    }

    private async Task TriggerForCharacter(TriggerPoint.TriggerType type, Character character)
    {
        foreach (string triggerId in character.TriggerPointIds)
        {
            TriggerPoint triggerPoint = await _databaseFactory.GetDatabase<ITriggerPointDatabase>().GetDocumentByIdAsync(triggerId)
                ?? throw new Exception($"TriggerPoint with ID {triggerId} not found for character {character.Id}");

            await triggerPoint.On(type);

            await _databaseFactory.SaveIfDirty(triggerPoint);
        }
    }

    private static void CleanupUnusedLocks()
    {
        foreach (KeyValuePair<(string userId, string characterId), SemaphoreSlim> kvp in _locks)
        {
            (string userId, string characterId) key = kvp.Key;
            SemaphoreSlim semaphore = kvp.Value;
            if (semaphore.Wait(0))
            {
                if (_locks.TryRemove(key, out _))
                {
                    semaphore.Dispose();
                }
                else
                {
                    semaphore.Release();
                }
            }
        }
    }

    private static bool TryLockCharacter(Character character, User user)
    {
        (string, string) key = (user.Id!, character.Id!);
        SemaphoreSlim semaphore = _locks.GetOrAdd(key, _ => new SemaphoreSlim(1, 1));
        if (!semaphore.Wait(0))
        {
            return false;
        }
        return true;
    }

    private static void LockCharacter(Character character, User user)
    {
        (string, string) key = (user.Id!, character.Id!);
        SemaphoreSlim semaphore = _locks.GetOrAdd(key, _ => new SemaphoreSlim(1, 1));
        if (semaphore.Wait(TimeSpan.FromMinutes(1)) == false)
        {
            throw new TimeoutException($"Timeout waiting for lock on character {character.Id} for user {user.Id}");
        }
    }

    private static void ReleaseLock(Character character, User user)
    {
        (string, string) key = (user.Id!, character.Id!);
        if (_locks.TryGetValue(key, out SemaphoreSlim? semaphore))
        {
            semaphore.Release();
        }
    }
}

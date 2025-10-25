using Akagi.Characters;
using Akagi.Characters.Conversations;
using Akagi.Communication;
using Akagi.Data;
using Akagi.Flow;
using Akagi.LLMs;
using Akagi.Receivers.Puppeteers;
using Akagi.Receivers.SystemProcessors;
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
    private readonly ISystemProcessorDatabase _systemProcessorDatabase;
    private readonly ILLMFactory _llmFactory;
    private readonly IDatabaseFactory _databaseFactory;
    private readonly ILogger<Receiver> _logger;

    public Receiver(IPuppeteerDatabase puppeteerDatabase,
                    ISystemProcessorDatabase systemProcessorDatabase,
                    ILLMFactory llmFactory,
                    IDatabaseFactory databaseFactory,
                    ILogger<Receiver> logger)
    {
        _puppeteerDatabase = puppeteerDatabase;
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

    public Task Reflect(Character character, User user)
    {
        throw new NotImplementedException();
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

            await using Context context = await GetContextAsync(character, user, communicator);
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
            await using Context context = await GetContextAsync(character, user, from);

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

            await using Context context = await GetContextAsync(character, user, from);

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
        await context.Puppeteer.Init(context, _systemProcessorDatabase);
        await context.Puppeteer.ProcessAsync();
    }

    private async Task<Context> GetContextAsync(Character character, User user, ICommunicator communicator)
    {
        return new Context()
        {
            Character = character,
            Conversation = character.GetCurrentConversation()!,
            User = user,
            Communicator = communicator,
            LLM = _llmFactory.Create(user),
            DatabaseFactory = _databaseFactory,
            Puppeteer = await _puppeteerDatabase.GetDocumentByIdAsync(character.PuppeteerId)
                        ?? throw new Exception($"Puppeteer with ID {character.PuppeteerId} not found for character {character.Id}"),
        };
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
        semaphore.Wait();
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

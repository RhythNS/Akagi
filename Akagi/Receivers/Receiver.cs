using Akagi.Characters;
using Akagi.Characters.Conversations;
using Akagi.Communication;
using Akagi.Data;
using Akagi.LLMs;
using Akagi.Receivers.Puppeteers;
using Akagi.Receivers.SystemProcessors;
using Akagi.Users;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;

namespace Akagi.Receivers;

internal class Receiver : IReceiver
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

    public async Task OnMessageRecieved(ICommunicator from, User user, Character character, Message message)
    {
        if (!TryLockCharacter(character, user))
        {
            await from.SendMessage(user, character, "Character is busy processing another message. Please try again later.");
            return;
        }

        try
        {
            await using Context context = new()
            {
                Character = character,
                Conversation = character.GetCurrentConversation()!,
                User = user,
                Communicator = from,
                LLM = _llmFactory.Create(user),
                DatabaseFactory = _databaseFactory,
            };

            context.Conversation.AddMessage(message);
            await ProcessCharacterMessage(character, context);
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
            if (from != null)
            {
                await from.SendMessage(user, character, "Character is busy processing another message. Please try again later.");
            }
            return;
        }

        try
        {
            if (from == null)
            {
                _logger.LogWarning("No communicator found for user {UserId}", user.Id);
                return;
            }

            await using Context context = new()
            {
                Character = character,
                Conversation = character.GetCurrentConversation()!,
                User = user,
                Communicator = from,
                LLM = _llmFactory.Create(user),
                DatabaseFactory = _databaseFactory
            };

            await ProcessCharacterMessage(character, context);
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

    public Task Reflect(Character character, User user)
    {
        throw new NotImplementedException();
    }

    private async Task ProcessCharacterMessage(Character character, Context context)
    {
        Puppeteer? puppeteer = await _puppeteerDatabase.GetDocumentByIdAsync(character.PuppeteerId)
                        ?? throw new Exception($"Puppeteer with ID {character.PuppeteerId} not found for character {character.Id}");

        await puppeteer.Init(context, _systemProcessorDatabase);
        await puppeteer.ProcessAsync();
    }

    public static void CleanupUnusedLocks()
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

    private static void ReleaseLock(Character character, User user)
    {
        (string, string) key = (user.Id!, character.Id!);
        if (_locks.TryGetValue(key, out SemaphoreSlim? semaphore))
        {
            semaphore.Release();
        }
    }
}

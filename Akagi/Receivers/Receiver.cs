using Akagi.Characters;
using Akagi.Characters.Conversations;
using Akagi.Communication;
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

    private readonly ICharacterDatabase _characterDatabase;
    private readonly IPuppeteerDatabase _puppeteerDatabase;
    private readonly ISystemProcessorDatabase _systemProcessorDatabase;
    private readonly ILLMFactory _llmFactory;
    private readonly ILogger<Receiver> _logger;

    public Receiver(ICharacterDatabase characterDatabase,
                    IPuppeteerDatabase puppeteerDatabase,
                    ISystemProcessorDatabase systemProcessorDatabase,
                    ILLMFactory llmFactory,
                    ILogger<Receiver> logger)
    {
        _characterDatabase = characterDatabase;
        _puppeteerDatabase = puppeteerDatabase;
        _systemProcessorDatabase = systemProcessorDatabase;
        _llmFactory = llmFactory;
        _logger = logger;
    }

    public async Task OnMessageRecieved(ICommunicator from, User user, Character character, Message message)
    {
        (string, string) key = (user.Id!, character.Id!);
        SemaphoreSlim semaphore = _locks.GetOrAdd(key, _ => new SemaphoreSlim(1, 1));

        if (!semaphore.Wait(0))
        {
            await from.SendMessage(user, character, "Character is busy processing another message. Please try again later.");
            return;
        }

        try
        {
            Context context = new()
            {
                Character = character,
                Conversation = character.GetCurrentConversation()!,
                User = user,
                Communicator = from,
                LLM = _llmFactory.Create(user),
            };

            character.GetCurrentConversation()!.AddMessage(message);

            Puppeteer? puppeteer = await _puppeteerDatabase.GetDocumentByIdAsync(character.PuppeteerId)
                ?? throw new Exception($"Puppeteer with ID {character.PuppeteerId} not found for character {character.Id}");

            await puppeteer.Init(context, _systemProcessorDatabase);
            await puppeteer.ProcessAsync();

            await _characterDatabase.SaveDocumentAsync(character);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing message for user {UserId} and character {CharacterId}", user.Id, character.Id);
            await from.SendMessage(user, character, "An error occurred while processing your message. Please try again later.");
        }
        finally
        {
            semaphore.Release();
        }
    }

    public Task OnMessageIgnored(Character character, User user)
    {
        throw new NotImplementedException();
    }

    public Task Reflect(Character character, User user)
    {
        throw new NotImplementedException();
    }
}

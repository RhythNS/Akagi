using Akagi.Characters;
using Akagi.Communication;
using Akagi.LLMs;
using Akagi.Puppeteers.Commands;
using Akagi.Puppeteers.Commands.Messages;
using Akagi.Puppeteers.SystemProcessors;
using Akagi.Users;
using Microsoft.Extensions.Logging;

namespace Akagi.Puppeteers;

internal class Puppeteer : IPuppeteer
{
    private readonly ILogger<Puppeteer> _logger;
    private readonly ICharacterDatabase _characterDatabase;
    private readonly ILLMFactory _llmFactory;

    public Puppeteer(ILogger<Puppeteer> logger, ICharacterDatabase characterDatabase, ILLMFactory llmFactory)
    {
        _logger = logger;
        _characterDatabase = characterDatabase;
        _llmFactory = llmFactory;
    }

    public async Task OnMessageRecieved(ICommunicator from, SystemProcessor processor, User user, Character character, string message)
    {
        // TODO: block messages if another message is being processed

        TextMessage textMessage = new()
        {
            From = Message.Type.User,
            Text = message,
            Time = DateTime.UtcNow,
        };

        character.GetLastConversation()!.AddMessage(textMessage);

        ILLM llm = _llmFactory.Create(user);

        bool shouldContinue = true;
        do
        {
            Command[] commands = await llm.GetNextSteps(processor, character, user);
            foreach (Command command in commands)
            {
                await command.Execute(new Command.Context
                {
                    Character = character,
                    Conversation = character.GetLastConversation()!
                });

                if (command is MessageCommand response)
                {
                    await from.SendMessage(user, character, response.GetMessage());
                }

                character.GetLastConversation()!.AddCommand(command, DateTime.Now, Message.Type.Character);

                shouldContinue &= command.ContinueAfterExecution;
            }
        } while (shouldContinue);

        await _characterDatabase.SaveDocumentAsync(character);
    }

    public Task OnMessageIgnored(SystemProcessor processor, Character character, User user)
    {
        throw new NotImplementedException();
    }

    public Task Reflect(SystemProcessor processor, Character character, User user)
    {
        throw new NotImplementedException();
    }
}

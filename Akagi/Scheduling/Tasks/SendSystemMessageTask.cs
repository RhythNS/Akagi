using Akagi.Characters;
using Akagi.Characters.Conversations;
using Akagi.Flow;
using Akagi.Receivers;
using Akagi.Users;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Akagi.Scheduling.Tasks;

internal class SendSystemMessageTask : OneShotTask
{
    public string UserId { get; set; } = string.Empty;
    public string CharacterId { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;

    protected async override Task ExecuteTaskAsync()
    {
        if (string.IsNullOrEmpty(UserId) || string.IsNullOrEmpty(CharacterId) || string.IsNullOrEmpty(Message))
        {
            throw new InvalidOperationException("UserId, CharacterId, and Message must be set before executing the task.");
        }
        ILogger<SendSystemMessageTask> logger = Globals.Instance.GetLogger<SendSystemMessageTask>();

        IUserDatabase userDatabase = Globals.Instance.ServiceProvider.GetRequiredService<IUserDatabase>();
        ICharacterDatabase characterDatabase = Globals.Instance.ServiceProvider.GetRequiredService<ICharacterDatabase>();

        User? user = await userDatabase.GetDocumentByIdAsync(UserId) ?? throw new InvalidOperationException($"User with ID {UserId} not found.");
        Character? character = await characterDatabase.GetCharacter(CharacterId) ?? throw new InvalidOperationException($"Character with ID {CharacterId} not found.");

        if (user == null || character == null)
        {
            logger.LogError("User or Character not found. UserId: {UserId}, CharacterId: {CharacterId}", UserId, CharacterId);
            return;
        }

        TextMessage message = new()
        {
            From = Characters.Conversations.Message.Type.System,
            Text = $"{TextMessage.SystemMessagePrefix}{Message}",
            Time = DateTime.UtcNow,
            VisibleTo = Characters.Conversations.Message.Type.System | Characters.Conversations.Message.Type.Character
        };

        IReceiver receiver = Globals.Instance.ServiceProvider.GetRequiredService<IReceiver>();
        await receiver.OnSystemEvent(character, user, message);
    }
}

using Akagi.Characters;
using Akagi.Characters.Conversations;
using Akagi.Characters.Groups;
using Akagi.Communication;
using Akagi.Users;

namespace Akagi.Life.Japanese.Calls;

/// <summary>
/// Hands a finished call report to the companion, who tells the user about it in her own voice.
/// </summary>
internal interface ICallDelivery
{
    /// <summary>
    /// Throws when the report cannot reach the user, so the job is not marked as delivered.
    /// </summary>
    public Task DeliverAsync(CallJob job);
}

internal class CallDelivery : ICallDelivery
{
    private readonly ICharacterDatabase _characters;
    private readonly IUserDatabase _users;
    private readonly ICommunicatorFactory _communicators;
    private readonly IProactiveDelivery _delivery;

    public CallDelivery(ICharacterDatabase characters, IUserDatabase users, ICommunicatorFactory communicators, IProactiveDelivery delivery)
    {
        _characters = characters;
        _users = users;
        _communicators = communicators;
        _delivery = delivery;
    }

    public async Task DeliverAsync(CallJob job)
    {
        Character character = await _characters.GetCharacter(job.DeliveryCharacterId)
            ?? throw new InvalidOperationException($"The character {job.DeliveryCharacterId} that should deliver call {job.Id} no longer exists.");
        User user = await _users.GetDocumentByIdAsync(job.UserId)
            ?? throw new InvalidOperationException($"User {job.UserId} of call {job.Id} not found.");

        // The receiver only logs a missing communicator; checked here so a lost report is not counted as delivered
        if (_communicators.Create(user.LastUsedCommunicator) == null)
        {
            throw new InvalidOperationException(
                $"No communicator called '{user.LastUsedCommunicator}' to reach user {user.Id}; the report stays readable with /calls {job.ShortId}.");
        }

        TextMessage message = new()
        {
            From = Message.Type.System,
            Time = DateTime.UtcNow,
            Text = CallJobs.BuildDeliveryInstruction(job),
            CharacterId = null
        };
        await _delivery.DeliverAsync(character, user, message);
    }
}

using Akagi.Characters.Conversations;
using Akagi.Communication;
using Akagi.Data;
using Akagi.Embeddings;
using Akagi.LLMs;
using Akagi.Users;

namespace Akagi.Characters.Groups;

internal class GroupContext : ContextBase
{
    public required CharacterGroup Group { get; set; }
    public required IReadOnlyList<Character> Members { get; set; }
    public required User User { get; set; }
    public required ICommunicator Communicator { get; set; }
    public required ILLMFactory LLMFactory { get; set; }
    public required CancellationToken CancellationToken { get; set; }
    public IEmbeddingService? EmbeddingService { get; set; }
    /// <summary>
    /// What started this group turn: a user message or a system event (routine, reminder, call report).
    /// </summary>
    public Receivers.Commands.TurnSource Turn { get; set; } = Receivers.Commands.TurnSource.Unknown;

    protected override Savable?[] ToTrack => [Group, User, .. Members];
}

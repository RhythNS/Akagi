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

    protected override Savable?[] ToTrack => [Group, User, .. Members];
}

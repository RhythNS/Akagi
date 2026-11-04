using Akagi.Characters;
using Akagi.Characters.Groups;
using Akagi.Communication;
using Akagi.Data;
using Akagi.Embeddings;
using Akagi.LLMs;
using Akagi.Users;

namespace Akagi.Receivers;

internal class Context : ContextBase
{
    public required Character Character { get; set; }
    public required Conversation Conversation { get; set; }
    public required User User { get; set; }
    public required ICommunicator Communicator { get; set; }
    public required ILLMFactory LLMFactory { get; set; }
    public CharacterGroup? Group { get; set; }
    public IEmbeddingService? EmbeddingService { get; set; }
    /// <summary>
    /// Set when a tool ended the conversation during this turn, so ConversationEnded triggers fire afterwards.
    /// </summary>
    public bool ConversationEnded { get; set; }
    /// <summary>
    /// When set, every model request made with this context adds what it used and cost, for callers that account for it.
    /// </summary>
    public List<LLMCallUsage>? UsageLog { get; set; }

    protected override Savable[] ToTrack => Group != null ? [Character, User, Group] : [Character, User];
}

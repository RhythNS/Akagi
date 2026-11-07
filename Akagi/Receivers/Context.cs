using Akagi.Characters;
using Akagi.Characters.Groups;
using Akagi.Communication;
using Akagi.Data;
using Akagi.Embeddings;
using Akagi.LLMs;
using Akagi.Receivers.Commands;
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
    /// <summary>
    /// What started this turn; decides which tools are offered and whether facts about the user may be recorded.
    /// </summary>
    public TurnSource Turn { get; set; } = TurnSource.Unknown;
    /// <summary>
    /// Counts and checks the tool calls of this turn.
    /// </summary>
    public ToolGuard Guard { get; } = new();
    /// <summary>
    /// How messages other group members wrote appear to this character's model. Keep leaves them as they are.
    /// </summary>
    public MemberSpeech OtherMembers { get; set; } = MemberSpeech.Keep;
    /// <summary>
    /// Names of the group members by character id, used to label their messages.
    /// </summary>
    public IReadOnlyDictionary<string, string> MemberNames { get; set; } = new Dictionary<string, string>();

    protected override Savable[] ToTrack => Group != null ? [Character, User, Group] : [Character, User];
}

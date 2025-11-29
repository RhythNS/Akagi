using Akagi.Characters;
using Akagi.Communication;
using Akagi.Data;
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

    protected override Savable[] ToTrack => [Character, User];
}

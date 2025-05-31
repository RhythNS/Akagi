using Akagi.Characters;
using Akagi.Communication;
using Akagi.Data;
using Akagi.LLMs;
using Akagi.Receivers.Puppeteers;
using Akagi.Users;

namespace Akagi.Receivers;

internal class Context : ContextBase
{
    public required Character Character { get; init; }
    public required Conversation Conversation { get; init; }
    public required User User { get; init; }
    public required ICommunicator Communicator { get; init; }
    public required ILLM LLM { get; init; }
    public required Puppeteer Puppeteer { get; init; }

    protected override Savable[] ToTrack => [Character, User, Puppeteer];
}

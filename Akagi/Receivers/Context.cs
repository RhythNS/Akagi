using Akagi.Characters;
using Akagi.Communication;
using Akagi.LLMs;
using Akagi.Users;

namespace Akagi.Receivers;

internal class Context
{
    public required Character Character { get; init; }
    public required Conversation Conversation { get; init; }
    public required User User { get; init; }
    public required ICommunicator Communicator { get; init; }
    public required ILLM LLM { get; init; }
}

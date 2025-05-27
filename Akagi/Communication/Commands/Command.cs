using Akagi.Characters;
using Akagi.Users;

namespace Akagi.Communication.Commands;

internal abstract class Command
{
    public class Context
    {
        public required Character Character { get; init; }
        public required User User { get; init; }
    }

    public abstract string Name { get; }

    private ICommunicator? _communicator;
    protected ICommunicator Communicator
    {
        get
        {
            if (_communicator == null)
            {
                throw new InvalidOperationException("Command has not been initialized with a communicator.");
            }
            return _communicator;
        }
    }

    public void Init(ICommunicator communicator)
    {
        _communicator = communicator;
    }
}

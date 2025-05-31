using Akagi.Characters;
using Akagi.Data;
using Akagi.Users;

namespace Akagi.Communication.Commands;

internal abstract class Command
{
    public class Context : ContextBase
    {
        public required Character Character { get; init; }
        public required User User { get; init; }
        protected override Savable[] ToTrack => [Character, User];
    }

    public abstract string Name { get; }

    public abstract string Description { get; }

    // TODO: implement this properly
    public virtual Type[] CompatibleFor => [typeof(ICommunicator)];

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

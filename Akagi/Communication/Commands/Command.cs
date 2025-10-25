using Akagi.Characters;
using Akagi.Data;
using Akagi.Users;

namespace Akagi.Communication.Commands;

internal abstract class Command
{
    public class Context : ContextBase
    {
        public Character? Character { get; init; }
        public required User User { get; init; }
        protected override Savable?[] ToTrack => [Character, User];
    }

    public abstract string Name { get; }

    public abstract string Description { get; }

    public virtual bool AdminOnly => false;

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

    public static string[] ParseArguments(string text)
    {
        List<string> args = [];
        int i = 0;
        while (i < text.Length)
        {
            if (text[i] == '{' && i + 1 < text.Length && text[i + 1] == '{')
            {
                int end = text.IndexOf("}}", i + 2, StringComparison.Ordinal);
                if (end == -1)
                {
                    args.Add(text.Substring(i + 2).Trim());
                    break;
                }
                args.Add(text.Substring(i + 2, end - (i + 2)).Trim());
                i = end + 2;
            }
            else if (!char.IsWhiteSpace(text[i]))
            {
                int start = i;
                while (i < text.Length && !char.IsWhiteSpace(text[i]))
                {
                    i++;
                }
                args.Add(text.Substring(start, i - start));
            }
            else
            {
                i++;
            }
        }

        return [.. args];
    }
}

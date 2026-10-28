using Akagi.Characters;
using Akagi.Characters.Groups;
using Akagi.Data;
using Akagi.Users;

namespace Akagi.Communication.Commands;

internal record CommandResult(bool Success, string? Error = null)
{
    public static CommandResult Ok => new(true);
    public static CommandResult Fail(string error) => new(false, error);
}

internal abstract class Command
{
    public class Context : ContextBase
    {
        public Character? Character { get; init; }
        public CharacterGroup? Group { get; init; }
        public required User User { get; init; }
        public Dictionary<string, string> Variables { get; init; } = [];
        protected override Savable?[] ToTrack => [Character, Group, User];
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

    /// <summary>
    /// The usage line in the <see cref="Bridge.Chat.Models.CommandUsage"/> grammar, read from the description by default. Override it
    /// when the description's usage is prose a client cannot turn into a form.
    /// </summary>
    public virtual string? Usage => Bridge.Chat.Models.CommandUsage.Split(Description).Usage;

    /// <summary>
    /// The folder below Commands the command lives in, such as "Life"; "General" for the top level.
    /// </summary>
    public string Category
    {
        get
        {
            const string root = "Akagi.Communication.Commands";
            string space = GetType().Namespace ?? string.Empty;
            if (space.StartsWith(root + ".", StringComparison.Ordinal))
            {
                return space[(root.Length + 1)..].Split('.')[0];
            }
            if (space == root || space.Length == 0)
            {
                return "General";
            }
            // "Akagi.Communication.TelegramComs.Commands" belongs to "Telegram"
            string[] parts = space.Split('.');
            string last = parts[^1] == "Commands" && parts.Length > 1 ? parts[^2] : parts[^1];
            return last.EndsWith("Coms", StringComparison.Ordinal) ? last[..^4] : last;
        }
    }

    /// <summary>
    /// What clients need to offer the command without typing it: summary, category and parameter forms.
    /// </summary>
    public Bridge.Chat.Models.Command Describe()
    {
        bool acceptsFiles = this is DocumentCommand;
        return new Bridge.Chat.Models.Command
        {
            Name = Name,
            Description = Description,
            Summary = Bridge.Chat.Models.CommandUsage.Split(Description).Summary,
            Category = Category,
            AcceptsFiles = acceptsFiles,
            AdminOnly = AdminOnly,
            Forms = Bridge.Chat.Models.CommandUsage.Parse(Name, Usage, acceptsFiles)
        };
    }

    /// <summary>
    /// The command named by the first word of <paramref name="text"/>. Matches the whole name, so "/upgrades"
    /// never runs "/upgrade"; a Telegram "@bot" suffix is ignored.
    /// </summary>
    public static T? Find<T>(IEnumerable<T> commands, string text) where T : Command
    {
        string name = CommandWord(text);
        return commands.FirstOrDefault(c => string.Equals(c.Name, name, StringComparison.InvariantCultureIgnoreCase));
    }

    /// <summary>
    /// Everything after the command word.
    /// </summary>
    public static string ArgumentText(string text)
    {
        string trimmed = text.TrimStart();
        int space = trimmed.IndexOfAny([' ', '\t', '\r', '\n']);
        return space < 0 ? string.Empty : trimmed[(space + 1)..].Trim();
    }

    private static string CommandWord(string text)
    {
        string trimmed = text.TrimStart();
        int space = trimmed.IndexOfAny([' ', '\t', '\r', '\n']);
        string word = space < 0 ? trimmed : trimmed[..space];
        int at = word.IndexOf('@');
        return at > 0 ? word[..at] : word;
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

    public static string[] ResolveVariables(string[] args, Dictionary<string, string> variables)
    {
        if (variables.Count == 0)
        {
            return args;
        }
        string[] resolved = new string[args.Length];
        for (int i = 0; i < args.Length; i++)
        {
            string arg = args[i];
            foreach (KeyValuePair<string, string> variable in variables)
            {
                arg = arg.Replace($"${variable.Key}", variable.Value, StringComparison.Ordinal);
            }
            resolved[i] = arg;
        }
        return resolved;
    }
}

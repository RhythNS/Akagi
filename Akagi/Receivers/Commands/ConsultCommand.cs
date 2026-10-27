using Akagi.Characters;
using Akagi.Characters.Conversations;
using Akagi.Characters.Groups;

namespace Akagi.Receivers.Commands;

/// <summary>
/// Lets a group's persona privately ask another member (a specialist subagent) and use the answer in its own reply.
/// The member's answer is returned as the tool result and is never shown to the user directly.
/// </summary>
internal class ConsultCommand : Command
{
    public const int MaxDepth = 2;

    private static readonly AsyncLocal<int> Depth = new();

    public override string Name => "Consult";

    public override string Description =>
        "Privately ask one of your advisors (another group member) for help, e.g. a tutor for a grammar breakdown or the health " +
        "monitor about sleep. You receive their answer and then reply to the user yourself, in your own voice.";

    public override bool ContinueAfterExecution => true;

    public override Argument[] GetDefaultArguments() =>
    [
        new Argument
        {
            Name = "Member",
            Description = "Name of the advisor to consult.",
            IsRequired = true,
            ArgumentType = Argument.Type.String
        },
        new Argument
        {
            Name = "Request",
            Description = "What you need from them, with the relevant context.",
            IsRequired = true,
            ArgumentType = Argument.Type.String
        }
    ];

    public override async Task<Command[]> Execute(Context context)
    {
        string memberName = RequireArgument("Member");
        string request = RequireArgument("Request");

        if (context.Group == null)
        {
            return Reply(context, "Consult only works inside a group; answer the user yourself.");
        }
        if (Depth.Value >= MaxDepth)
        {
            return Reply(context, "Consultations are already nested too deeply; answer with what you know.");
        }

        ICharacterDatabase characterDatabase = context.DatabaseFactory.GetDatabase<ICharacterDatabase>();
        List<Character> members = [];
        foreach (string id in context.Group.MemberCharacterIds.Where(id => id != context.Character.Id))
        {
            Character? member = await characterDatabase.GetCharacter(id);
            if (member != null)
            {
                members.Add(member);
            }
        }

        Character? advisor = FindMember(members, memberName);
        if (advisor == null)
        {
            string available = members.Count == 0 ? "none" : string.Join(", ", members.Select(m => m.Name));
            return Reply(context, $"There is no advisor called '{memberName}'. Available: {available}.");
        }

        context.Conversation.AddMessage(new TextMessage
        {
            From = Message.Type.System,
            Time = DateTime.UtcNow,
            CharacterId = null,
            Text = $"{TextMessage.SystemMessagePrefix}[Consultation for {advisor.Name} from {context.Character.Name}] {request}" + Environment.NewLine +
                   $"{advisor.Name}: answer {context.Character.Name} directly and concisely; {context.Character.Name} will talk to the user."
        });
        int before = context.Conversation.Messages.Count;

        Depth.Value++;
        try
        {
            await using Context advisorContext = new()
            {
                Character = advisor,
                Conversation = context.Conversation,
                User = context.User,
                Communicator = new GroupInternalCommunicator(context.Communicator, context.Group, advisor, context.User, forwardToUser: false),
                LLMFactory = context.LLMFactory,
                DatabaseFactory = context.DatabaseFactory,
                EmbeddingService = context.EmbeddingService,
                Group = context.Group
            };
            await GroupMemberRunner.RunAsync(advisor, advisorContext);
        }
        finally
        {
            Depth.Value--;
        }

        string answer = CollectAnswer(context.Conversation.Messages.Skip(before), advisor.Id);
        return Reply(context, string.IsNullOrWhiteSpace(answer)
            ? $"{advisor.Name} had nothing to add."
            : $"{advisor.Name} answered:{Environment.NewLine}{answer}");
    }

    internal static Character? FindMember(IReadOnlyList<Character> members, string name)
    {
        string trimmed = name.Trim().TrimStart('@');
        return members.FirstOrDefault(m => string.Equals(m.Name, trimmed, StringComparison.OrdinalIgnoreCase))
            ?? members.FirstOrDefault(m => m.Name.Contains(trimmed, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// The advisor's spoken text plus the results of tools it used, so the persona sees what was looked up.
    /// </summary>
    internal static string CollectAnswer(IEnumerable<Message> messages, string? advisorId)
    {
        List<string> parts = [];
        foreach (Message message in messages.Where(m => m.CharacterId == advisorId))
        {
            switch (message)
            {
                case TextMessage text when message.From == Message.Type.Character:
                    parts.Add(text.Text);
                    break;
                case CommandMessage command when command.Command is not ConsultCommand:
                    parts.Add($"({command.Command.Name}: {command.Output})");
                    break;
            }
        }
        return string.Join(Environment.NewLine, parts).Trim();
    }

    private Command[] Reply(Context context, string output)
    {
        context.Conversation.AddMessage(CreateCommandMessage(output));
        return [];
    }
}

using Akagi.Flow;
using Microsoft.Extensions.Logging;

namespace Akagi.Receivers.Commands;

internal class LaunchNukesCommand : Command
{
    public override string Name => "LaunchNukes";

    public override string Description => "Launches nuclear missiles.";

    public override Argument[] GetDefaultArguments() =>
    [
        new Argument
        {
            Name = "Target",
            Description = "The target location for the nuclear missiles.",
            IsRequired = true,
            ArgumentType = Argument.Type.String
        },
        new Argument
        {
            Name = "WarheadCount",
            Description = "The number of warheads to launch.",
            IsRequired = true,
            ArgumentType = Argument.Type.Int
        }
    ];

    public override bool ContinueAfterExecution => false;

    public override Task Execute(Context context)
    {
        if (Arguments.Length < 2 ||
            string.IsNullOrWhiteSpace(Arguments[0].Value) ||
            string.IsNullOrWhiteSpace(Arguments[1].Value))
        {
            throw new InvalidOperationException("Insufficient arguments provided for LaunchNukesCommand.");
        }
        string target = Arguments[0].Value;

        int? warheadCount = Arguments[1].IntValue;
        if (warheadCount == null)
        {
            throw new InvalidOperationException("Invalid warhead count provided for LaunchNukesCommand.");
        }

        ILogger<LaunchNukesCommand> logger = Globals.Instance.GetLogger<LaunchNukesCommand>();
        logger.LogCritical("Nuclear missiles launched at {Target} with {WarheadCount} warheads.", target, warheadCount);

        string output = $"Nuclear missiles have been launched at {target} with {warheadCount} warheads.";
        context.Conversation.AddMessage(CreateCommandMessage(output));

        return Task.CompletedTask;
    }
}

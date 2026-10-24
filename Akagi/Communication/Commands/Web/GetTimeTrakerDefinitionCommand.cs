using Akagi.Connectors.TimeTracker;

namespace Akagi.Communication.Commands.Web;

internal class GetTimeTrakerDefinitionCommand : WebCommand
{
    public override string Name => "/GetTimeTrackerDefinition";

    public override string Description => "Looks up a word in the JPDict database. Usage: /jpDictLookup <word>";

    private ITimeTrackerConnector _connector;

    public GetTimeTrakerDefinitionCommand(ITimeTrackerConnector connector)
    {
        _connector = connector;
    }

    public override async Task<CommandResult> ExecuteAsync(Context context, string[] args)
    {
        string? googleId = await TryGetGoogleIdOrPrintError(context);
        if (googleId == null)
        {
            return CommandResult.Fail(UserNotConfiguredError);
        }

        TimeTrackerUserConfig userConfig = new() { GoogleId = googleId };

        if (args.Length == 0)
        {
            List<Definition> definitions = await _connector.GetDefinitionsAsync(userConfig);
            
            // Do like a tostring method for everything. Should probs be using json serilizing
            // which can be reused in the acctual command for the llm.

            return CommandResult.Ok;
        }

        throw new NotImplementedException();
    }
}

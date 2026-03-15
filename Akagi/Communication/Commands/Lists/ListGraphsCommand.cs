using Akagi.Graphs;

namespace Akagi.Communication.Commands.Lists;

internal class ListGraphsCommand : TextCommand
{
    public override string Name => "/listGraphs";

    public override string Description => "Lists all graphs. Usage: /listGraphs";

    public override async Task<CommandResult> ExecuteAsync(Context context, string[] args)
    {
        GraphInstanceDatabase graphDatabase = context.DatabaseFactory.GetDatabase<GraphInstanceDatabase>();
        GraphInstance[] instances = await graphDatabase.GetGraphs(context.User.Id!);

        string response = string.Join("\n", instances.Select(i => $"{i.GraphId}:{i.Name}"));

        await Communicator.SendMessage(context.User, response);
        return CommandResult.Ok;
    }
}

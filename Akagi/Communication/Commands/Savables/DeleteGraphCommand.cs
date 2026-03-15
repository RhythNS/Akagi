using Akagi.Data;
using Akagi.Graphs;

namespace Akagi.Communication.Commands.Savables;

internal class DeleteGraphCommand : TextCommand
{
    public override string Name => "/deleteGraph";

    public override string Description => "Deletes a graph and all its associated savables. Usage: /deleteGraph <graph id> <graph name>";

    private readonly IDatabaseFactory _databaseFactory;

    public DeleteGraphCommand(IDatabaseFactory databaseFactory)
    {
        _databaseFactory = databaseFactory;
    }

    public override async Task<CommandResult> ExecuteAsync(Context context, string[] args)
    {
        if (args.Length < 2)
        {
            await Communicator.SendMessage(context.User, "Usage: /deleteGraph <graph id> <graph name>");
            return CommandResult.Fail("Invalid arguments.");
        }
        string graphId = args[0];
        string graphName = args[1];

        IGraphInstanceDatabase graphInstanceDatabase = _databaseFactory.GetDatabase<IGraphInstanceDatabase>();
        GraphInstance? graphInstance = await graphInstanceDatabase.GetGraph(graphId, graphName, context.User.Id!);
        if (graphInstance == null)
        {
            await Communicator.SendMessage(context.User, $"Graph with ID '{graphId}' and name '{graphName}' not found.");
            return CommandResult.Fail($"Graph '{graphId}:{graphName}' not found.");
        }
        foreach (GraphInstance.SavableInfo savableInfo in graphInstance.SavableInfos)
        {
            IDatabase savableDatabase = _databaseFactory.GetDatabase(savableInfo.CollectionName);
            Type savableType = savableDatabase.GetType().GetInterfaces()
                .First(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IDatabase<>))
                .GetGenericArguments()[0];

            System.Reflection.MethodInfo deleteMethod = typeof(IDatabase<>)
                .MakeGenericType(savableType)
                .GetMethod(nameof(IDatabase<>.DeleteDocumentByIdAsync))!;

            Task deleteTask = (Task)deleteMethod.Invoke(savableDatabase, [savableInfo.SavableId])!;
            await deleteTask;
        }
        await graphInstanceDatabase.DeleteDocumentByIdAsync(graphInstance.Id!);

        await Communicator.SendMessage(context.User, "Deleted Graph successfully");
        return CommandResult.Ok;
    }
}

using Akagi.Connectors.Tatoeba;

namespace Akagi.Communication.Commands.Examples;

internal class ExampleSentenceCommand : TextCommand
{
    private readonly ITatoebaConnector _tatoebaConnector;

    public override string Name => "/exampleSentence";

    public override string Description => "Retrieves an example based on the user's configuration.";

    public ExampleSentenceCommand(ITatoebaConnector tatoebaConnector)
    {
        _tatoebaConnector = tatoebaConnector;
    }

    public override async Task ExecuteAsync(Context context, string[] args)
    {
        TatoebaUserConfig? userConfig = context.User.GetConfig<TatoebaUserConfig>();
        if (userConfig == null)
        {
            await Communicator.SendMessage(context.User, "Configuration is not set. Call /exampleConfigure first.");
            return;
        }

        if (args.Length < 1)
        {
            await Communicator.SendMessage(context.User, "Provide what you would like to have an example of.");
            return;
        }

        string example = await _tatoebaConnector.GetExample(args[0], userConfig);

        await Communicator.SendMessage(context.User, example);
    }
}

using Akagi.Connectors.Desu;

namespace Akagi.Communication.Commands.JPDicts;

internal class JPDictLookupCommand : TextCommand
{
    private readonly IDesuConnector _desuConnector;

    public override string Name => "/jpDictLookup";

    public override string Description => "Looks up a word in the JPDict database. Usage: /jpDictLookup <word>";

    public JPDictLookupCommand(IDesuConnector desuConnector)
    {
        _desuConnector = desuConnector;
    }

    public override async Task ExecuteAsync(Context context, string[] args)
    {
        DesuUserConfig? desuUserConfig = context.User.GetConfig<DesuUserConfig>();
        if (desuUserConfig == null)
        {
            await Communicator.SendMessage(context.User, "Configuration is not set. Call /jpDictConfigure first");
            return;
        }

        if (args.Length == 0)
        {
            await Communicator.SendMessage(context.User, "Please provide a word to look up.");
            return;
        }

        string word = args[0];
        string result = _desuConnector.Lookup(word, desuUserConfig);

        await Communicator.SendMessage(context.User, result);
    }
}

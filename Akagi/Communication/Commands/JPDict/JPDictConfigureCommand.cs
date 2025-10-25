using Akagi.Connectors.Desu;

namespace Akagi.Communication.Commands.JPDict;

internal class JPDictConfigureCommand : TextCommand
{
    public override string Name => "/jpDictConfigure";
    public override string Description => "Configures the JPDict commands.";
    public override async Task ExecuteAsync(Context context, string[] args)
    {
        if (args.Length != 2)
        {
            await Communicator.SendMessage(context.User, "Usage: /jpDictConfigure <defaultPrint> <language>");
            return;
        }

        string defaultPrint = args[0].ToLowerInvariant();
        string language = args[1].ToLowerInvariant();

        DesuUserConfig userConfig = new()
        {
            DefaultPrint = defaultPrint,
            Language = language
        };

        context.User.SetConfig(userConfig);

        await Communicator.SendMessage(context.User, $"JPDict configuration updated: Default Print = {defaultPrint}, Language = {language}");
    }
}

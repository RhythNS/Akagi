using Akagi.Flow;
using Akagi.Receivers;
using Microsoft.Extensions.DependencyInjection;

namespace Akagi.Communication.Commands.ActiveCharacters;

internal class TriggerReflectCommand : TextCommand
{
    public override string Name => "/triggerReflect";

    public override string Description => "Command to trigger the reflect action for an active character. Usage: TriggerReflect <name>";

    public override async Task ExecuteAsync(Context context, string[] args)
    {
        if (context.Character is null)
        {
            await Communicator.SendMessage(context.User, "You need to have an active character to use this command.");
        }

        if (args.Length < 1)
        {
            await Communicator.SendMessage(context.User, "Please provide the name of the reflector to trigger.");
            return;
        }

        string reflectorName = args[0];

        await Globals.Instance.ServiceProvider.GetRequiredService<IReceiver>()
            .Reflect(context.Character!, context.User, reflectorName);

        await Communicator.SendMessage(context.User, "Reflect action has been triggered.");
    }
}

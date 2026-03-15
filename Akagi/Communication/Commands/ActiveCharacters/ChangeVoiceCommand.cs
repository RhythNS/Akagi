namespace Akagi.Communication.Commands.ActiveCharacters;

internal class ChangeVoiceCommand : TextCommand
{
    public override string Name => "/changeVoice";

    public override string Description => "Change the character's voice. Usage: /changeVoice <VoiceId> <VoiceModelId>";

    public override async Task<CommandResult> ExecuteAsync(Context context, string[] args)
    {
        if (context.Character == null)
        {
            await Communicator.SendMessage(context.User, "You must have an active character to change the voice.");
            return CommandResult.Fail("No active character.");
        }

        if (args.Length != 2)
        {
            await Communicator.SendMessage(context.User, "Invalid arguments. Usage: /changeVoice <VoiceId> <VoiceModelId>");
            return CommandResult.Fail("Invalid arguments.");
        }

        string voice = args[0];
        string voiceModel = args[1];

        context.Character.VoiceId = voice;
        context.Character.VoiceModelId = voiceModel;

        await Communicator.SendMessage(context.User, $"Character voice changed to VoiceId: {voice}, VoiceModelId: {voiceModel}");
        return CommandResult.Ok;
    }
}

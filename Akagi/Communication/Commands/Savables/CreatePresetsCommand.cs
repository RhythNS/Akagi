
using Akagi.Characters.Presets;

namespace Akagi.Communication.Commands.Savables;

internal class CreatePresetsCommand : TextCommand
{
    public override string Name => "/createPresets";

    public override string Description => "Creates all defined presets for the current user.";

    private readonly IPresetCreator _presetCreator;

    public CreatePresetsCommand(IPresetCreator presetCreator)
    {
        _presetCreator = presetCreator;
    }

    public override async Task ExecuteAsync(Context context, string[] args)
    {
        await _presetCreator.CreateForUser(context.User.Id!);
        await Communicator.SendMessage(context.User, "Presets created!");
    }
}

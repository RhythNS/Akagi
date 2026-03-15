using Akagi.TTSs;
using Akagi.TTSs.Inworld;

namespace Akagi.Communication.Commands.TTS;

internal class CreateSynthSpeechCommand : TextCommand
{
    public override string Name => "/CreateSynthSpeech";

    public override string Description => "Creates a new text to speech audio file. Usage <text> <voice> <model>";

    private IInworldTTSClient _tts;

    public CreateSynthSpeechCommand(IInworldTTSClient tTS)
    {
        _tts = tTS;
    }

    public override async Task<CommandResult> ExecuteAsync(Context context, string[] args)
    {
        if (args.Length != 3)
        {
            await Communicator.SendMessage(context.User, "Usage: /CreateSynthSpeech <text> <voice> <model>");
            return CommandResult.Fail("Invalid arguments.");
        }
        string text = args[0];
        string voice = args[1];
        string model = args[2];

        TTSResult speech = await _tts.SynthesizeSpeechAsync(text, voice, model);
        await using MemoryStream memoryStream = new MemoryStream(speech.AudioContent);
        await Communicator.SendAudio(context.User, memoryStream, $"audio{speech.AudioEncoding.ToFile()}");
        return CommandResult.Ok;
    }
}

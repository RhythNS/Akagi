using Akagi.Characters.VoiceClips;
using Akagi.Flow;
using Akagi.Receivers.Commands.Messages;
using Akagi.TTSs;
using Akagi.TTSs.Inworld;
using Microsoft.Extensions.DependencyInjection;

namespace Akagi.Receivers.Commands;

internal class RequestTextToSpeechCommand : Command
{
    public override string Name => "RequestTextToSpeech";

    public override string Description => "Converts text to speech.";

    public override Argument[] GetDefaultArguments() =>
    [
        new Argument
        {
            Name = "EmotionTag",
            Description = "The chosen global emotion tag (e.g., '[happy]', '[angry]').",
            IsRequired = false,
            ArgumentType = Argument.Type.String
        },
        new Argument
        {
            Name = "TransformedText",
            Description = "The fully normalized text, containing any *emphasis*, /IPA/ phonemes, non-verbals like [sigh], and <break time=\"1s\" /> tags.",
            IsRequired = true,
            ArgumentType = Argument.Type.String
        }
    ];

    public override bool ContinueAfterExecution => false;

    public override async Task<Command[]> Execute(Context context)
    {
        if (context.Character.AllowVoice == false)
        {
            context.Conversation.AddMessage(CreateCommandMessage("The character has voice generation disabled."));
            return [];
        }

        string? text = Arguments.FirstOrDefault(arg => string.Equals(arg.Name, "TransformedText", StringComparison.OrdinalIgnoreCase))?.Value;
        if (string.IsNullOrEmpty(text))
        {
            throw new ArgumentException("TransformedText argument is required and cannot be null or empty.");
        }
        string emotionTag = Arguments.FirstOrDefault(arg => string.Equals(arg.Name, "EmotionTag", StringComparison.OrdinalIgnoreCase))?.Value ?? string.Empty;

        string ssml = $"<speak>{emotionTag} {text}</speak>";

        IInworldTTSClient tts = Globals.Instance.ServiceProvider.GetRequiredService<IInworldTTSClient>();
        TTSResult result = await tts.SynthesizeSpeechAsync(ssml, context.Character.VoiceId, context.Character.VoiceModelId);

        IVoiceClipsDatabase voiceClipsDatabase = Globals.Instance.ServiceProvider.GetRequiredService<IVoiceClipsDatabase>();
        VoiceClip voiceClip = new()
        {
            AudioEncoding = result.AudioEncoding,
            CharacterId = context.Character.Id!,
            CharacterName = context.Character.Name,
            Text = ssml,
        };
        using MemoryStream audioStream = new(result.AudioContent);
        await voiceClipsDatabase.SaveFileAsync(voiceClip, audioStream);

        string output = $"[VoiceClipId:{voiceClip.Id}]";
        context.Conversation.AddMessage(CreateCommandMessage(output));

        ICommandFactory commandFactory = Globals.Instance.ServiceProvider.GetRequiredService<ICommandFactory>();

        VoiceMessageCommand voiceMessageCommand = commandFactory.Create<VoiceMessageCommand>();
        voiceMessageCommand.SetMessage(ssml, voiceClip.Id!);
        voiceMessageCommand.From = From;

        return [voiceMessageCommand];
    }
}

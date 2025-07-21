using Akagi.Connectors.Tatoeba;

namespace Akagi.Communication.Commands.Examples;

internal class ExampleConfigureCommand : TextCommand
{
    public override string Name => "/exampleConfigure";

    public override string Description => "Configures the example commands.";

    public override async Task ExecuteAsync(Context context, string[] args)
    {
        if (args.Length < 3)
        {
            await Communicator.SendMessage(context.User, "Usage: /exampleConfigure <maxSentences> <targetLanguage> <translationLanguage>");
            return;
        }

        if (!int.TryParse(args[0], out int maxSentences) || maxSentences <= 0)
        {
            await Communicator.SendMessage(context.User, "Invalid maxSentences. It must be a positive integer.");
            return;
        }

        string targetLanguage = args[1];
        string translationLanguage = args[2];

        TatoebaUserConfig userConfig = new()
        {
            MaxSentences = maxSentences,
            TargetLanguage = targetLanguage,
            TranslationLanguage = translationLanguage
        };

        context.User.SetConfig(userConfig);

        await Communicator.SendMessage(context.User, $"Example configuration updated:\n" +
            $"Max Sentences: {maxSentences}\n" +
            $"Target Language: {targetLanguage}\n" +
            $"Translation Language: {translationLanguage}");
    }
}

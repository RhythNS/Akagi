namespace Akagi.Connectors.Tatoeba;

internal class TatoebaUserConfig
{
    public required string TargetLanguage { get; set; }
    public required string TranslationLanguage { get; set; }
    public required int MaxSentences { get; set; }
}

using System.Text.Json.Serialization;

namespace Akagi.Characters.Cards;

internal class RawCard
{
    [JsonPropertyName("data")]
    public InnerData Data { get; set; } = new ();

    [JsonPropertyName("spec")]
    public string Spec { get; set; } = string.Empty;

    [JsonPropertyName("spec_version")]
    public string SpecVersion { get; set; } = string.Empty;

    public class InnerData
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("description")]
        public string Description { get; set; } = string.Empty;

        [JsonPropertyName("personality")]
        public string Personality { get; set; } = string.Empty;

        [JsonPropertyName("first_mes")]
        public string FirstMes { get; set; } = string.Empty;

        [JsonPropertyName("mes_example")]
        public string MesExample { get; set; } = string.Empty;

        [JsonPropertyName("scenario")]
        public string Scenario { get; set; } = string.Empty;

        [JsonPropertyName("creator_notes")]
        public string CreatorNotes { get; set; } = string.Empty;

        [JsonPropertyName("system_prompt")]
        public string SystemPrompt { get; set; } = string.Empty;

        [JsonPropertyName("post_history_instructions")]
        public string PostHistoryInstructions { get; set; } = string.Empty;

        [JsonPropertyName("alternate_greetings")]
        public string[] AlternateGreetings { get; set; } = [];

        [JsonPropertyName("tags")]
        public string[] Tags { get; set; } = [];

        [JsonPropertyName("creator")]
        public string Creator { get; set; } = string.Empty;

        [JsonPropertyName("character_version")]
        public string CharacterVersion { get; set; } = string.Empty;

        [JsonPropertyName("extensions")]
        public Dictionary<string, object> Extensions { get; set; } = [];
    }
}

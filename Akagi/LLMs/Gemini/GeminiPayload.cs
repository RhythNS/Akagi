﻿using System.Text.Json.Serialization;

namespace Akagi.LLMs.Gemini;

internal class GeminiPayload
{
    [JsonPropertyName("system_instruction")]
    public SystemInstruction Instruction { get; set; } = new();
    [JsonPropertyName("contents")]
    public Content[] Contents { get; set; } = [];
    [JsonPropertyName("tools")]
    public Tool[] Tools { get; set; } = [];

    internal class SystemInstruction
    {
        [JsonPropertyName("parts")]
        public Part[] Parts { get; set; } = [];
    }

    internal class Content
    {
        [JsonPropertyName("parts")]
        public Part[] Parts { get; set; } = [];
        [JsonPropertyName("role")]
        public string Role { get; set; } = string.Empty;
    }

    internal class Part
    {
        [JsonPropertyName("text")]
        public string Text { get; set; } = string.Empty;
    }

    internal class Tool
    {
        [JsonPropertyName("function_declarations")]
        public FunctionDeclaration[] FunctionDeclarations { get; set; } = [];
    }

    internal class FunctionDeclaration
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;
        [JsonPropertyName("description")]
        public string Description { get; set; } = string.Empty;
        [JsonPropertyName("parameters")]
        public Dictionary<string, object> Parameters { get; set; } = [];
    }
}

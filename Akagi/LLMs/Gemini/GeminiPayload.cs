using System.Text.Json.Serialization;

namespace Akagi.LLMs.Gemini;

internal class GeminiPayload
{
    [JsonPropertyName("system_instruction")]
    public SystemInstruction Instruction { get; set; } = new();
    [JsonPropertyName("contents")]
    public Content[] Contents { get; set; } = [];
    [JsonPropertyName("tool_config")]
    public ToolConfig ToolConf { get; set; } = new();
    [JsonPropertyName("tools")]
    public Tool[]? Tools { get; set; } = [];

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

    [JsonDerivedType(typeof(TextPart)),
        JsonDerivedType(typeof(FunctionCallPart)),
        JsonDerivedType(typeof(FunctionResponsePart))]
    internal abstract class Part;

    internal class TextPart : Part
    {
        [JsonPropertyName("text")]
        public string? Text { get; set; } = null;
    }

    internal class FunctionCallPart : Part
    {
        [JsonPropertyName("function_call")]
        public FunctionCall? FunctionCall { get; set; } = null;
    }

    internal class FunctionCall
    {
        [JsonPropertyName("id")]
        public string? Id { get; set; } = null;
        [JsonPropertyName("name")]
        public required string Name { get; set; }
        [JsonPropertyName("args")]
        public Dictionary<string, object>? Args { get; set; } = null;
    }

    internal class FunctionResponsePart : Part
    {
        [JsonPropertyName("function_response")]
        public FunctionResponse? FunctionResponse { get; set; } = null;
    }

    internal class FunctionResponse
    {
        [JsonPropertyName("id")]
        public string? Id { get; set; } = null;
        [JsonPropertyName("name")]
        public required string Name { get; set; }
        [JsonPropertyName("response")]
        public required Dictionary<string, object> Response { get; set; }
    }

    internal class ToolConfig
    {
        [JsonPropertyName("function_calling_config")]
        public FunctionCallingConfig FunctionCalling { get; set; } = new();
    }

    internal class FunctionCallingConfig
    {
        [JsonPropertyName("mode")]
        public FunctionCallingMode Mode { get; set; } = FunctionCallingMode.AUTO;
    }

    internal enum FunctionCallingMode
    {
        /// <summary>
        /// Unspecified function calling mode. This value should not be used.
        /// </summary>
        MODE_UNSPECIFIED,
        /// <summary>
        /// Default model behavior, model decides to predict either a function call or a natural language response.
        /// </summary>
        AUTO,
        /// <summary>
        /// Model is constrained to always predicting a function call only. If "allowedFunctionNames" are set, the
        /// predicted function call will be limited to any one of "allowedFunctionNames", else the predicted function
        /// call will be any one of the provided "functionDeclarations".
        /// </summary>
        ANY,
        /// <summary>
        /// Model will not predict any function call. Model behavior is same as when not passing any function declarations.
        /// </summary>
        NONE,
        /// <summary>
        /// Model decides to predict either a function call or a natural language response, but will validate function calls
        /// with constrained decoding. If "allowedFunctionNames" are set, the predicted function call will be limited to any
        /// one of "allowedFunctionNames", else the predicted function call will be any one of the provided "functionDeclarations".
        /// </summary>
        VALIDATED
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

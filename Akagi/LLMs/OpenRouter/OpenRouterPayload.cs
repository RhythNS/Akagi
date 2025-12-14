using System.Text.Json.Serialization;

namespace Akagi.LLMs.OpenRouter;

internal class OpenRouterPayload
{
    [JsonPropertyName("messages")]
    public Message[]? Messages { get; set; } = null;
    [JsonPropertyName("prompt")]
    public string? Prompt { get; set; } = null;
    [JsonPropertyName("model")]
    public string? Model { get; set; } = null;
    [JsonPropertyName("response_format")]
    public ResponseFormat? Response { get; set; } = null;
    [JsonPropertyName("stop")]
    public object? Stop { get; set; } = null; // Can be string or string[]
    [JsonPropertyName("stream")]
    public bool? Stream { get; set; } = null;
    [JsonPropertyName("max_tokens")]
    public int? MaxTokens { get; set; } = null;
    [JsonPropertyName("temperature")]
    public double? Temperature { get; set; } = null;
    [JsonPropertyName("tools")]
    public Tool[]? Tools { get; set; } = null;
    [JsonPropertyName("tool_choice")]
    public object? ToolChoice { get; set; } = null; // Can be ToolChoiceEnum or ToolChoiceObject
    [JsonPropertyName("seed")]
    public int? Seed { get; set; } = null;
    [JsonPropertyName("top_p")]
    public double? TopP { get; set; } = null;
    [JsonPropertyName("top_k")]
    public int? TopK { get; set; } = null;
    [JsonPropertyName("frequency_penalty")]
    public double? FrequencyPenalty { get; set; } = null;
    [JsonPropertyName("presence_penalty")]
    public double? PresencePenalty { get; set; } = null;
    [JsonPropertyName("repetition_penalty")]
    public double? RepetitionPenalty { get; set; } = null;
    [JsonPropertyName("logit_bias")]
    public Dictionary<int, int>? LogitBias { get; set; } = null;
    [JsonPropertyName("top_logprobs")]
    public int? TopLogprobs { get; set; } = null;
    [JsonPropertyName("min_p")]
    public double? MinP { get; set; } = null;
    [JsonPropertyName("top_a")]
    public double? TopA { get; set; } = null;
    [JsonPropertyName("prediction")]
    public Prediction? Pred { get; set; } = null;
    [JsonPropertyName("transforms")]
    public string[]? Transforms { get; set; } = null;
    [JsonPropertyName("models")]
    public string[]? Models { get; set; } = null;
    [JsonPropertyName("route")]
    public string? Route { get; set; } = null;
    [JsonPropertyName("provider")]
    public object? Provider { get; set; } = null; // ProviderPreferences type not defined in spec
    [JsonPropertyName("user")]
    public string? User { get; set; } = null;
    [JsonPropertyName("debug")]
    public DebugOptions? Debug { get; set; } = null;

    internal class ResponseFormat
    {
        [JsonPropertyName("type")]
        public string Type { get; set; } = "json_object";
    }

    [JsonDerivedType(typeof(UserMessage)),
        JsonDerivedType(typeof(ToolRequestMessage)),
        JsonDerivedType(typeof(ToolResponseMessage))]
    internal abstract class Message
    {
        [JsonPropertyName("role")]
        public required MessageRoleEnum Role { get; set; }
        [JsonPropertyName("name")]
        public string? Name { get; set; } = null;
    }

    [JsonConverter(typeof(JsonStringEnumConverter))]
    internal enum MessageRoleEnum
    {
        [JsonPropertyName("user")]
        User,
        [JsonPropertyName("assistant")]
        Assistant,
        [JsonPropertyName("system")]
        System,
    }

    internal class UserMessage : Message
    {
        [JsonPropertyName("content")]
        public object? Content { get; set; } = null; // Can be string or ContentPart[]
    }

    internal class ToolRequestMessage : Message
    {
        [JsonPropertyName("content")]
        public object? Content { get; set; } = null; // Can be string or ContentPart[]
        [JsonPropertyName("tool_calls")]
        public required ToolCall[] ToolCalls { get; set; }
    }

    internal class ToolResponseMessage : Message
    {
        [JsonPropertyName("content")]
        public string? Content { get; set; } = null;
        [JsonPropertyName("tool_call_id")]
        public required string ToolCallId { get; set; }
    }

    [JsonDerivedType(typeof(TextContent)),
        JsonDerivedType(typeof(ImageContentPart))]
    internal abstract class ContentPart
    {
        [JsonPropertyName("type")]
        public required string Type { get; set; }
    }

    internal class TextContent : ContentPart
    {
        [JsonPropertyName("text")]
        public string Text { get; set; } = string.Empty;
    }

    internal class ImageContentPart : ContentPart
    {
        [JsonPropertyName("image_url")]
        public ImageUrl ImageUrl { get; set; } = new();
    }

    internal class ImageUrl
    {
        [JsonPropertyName("url")]
        public string Url { get; set; } = string.Empty;
        [JsonPropertyName("detail")]
        public string? Detail { get; set; } = null;
    }

    internal class Tool
    {
        [JsonPropertyName("type")]
        public string Type { get; set; } = "function";
        [JsonPropertyName("function")]
        public FunctionDescription Function { get; set; } = new();
    }

    internal class FunctionDescription
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;
        [JsonPropertyName("description")]
        public string? Description { get; set; } = null;
        [JsonPropertyName("parameters")]
        public object Parameters { get; set; } = new { };
    }

    [JsonConverter(typeof(JsonStringEnumConverter))]
    internal enum ToolChoiceEnum
    {
        [JsonPropertyName("auto")]
        Auto,
        [JsonPropertyName("none")]
        None,
        [JsonPropertyName("required")]
        Required
    }

    internal class ToolChoiceObject
    {
        [JsonPropertyName("type")]
        public string Type { get; set; } = "function";
        [JsonPropertyName("function")]
        public ToolChoiceFunction Function { get; set; } = new();
    }

    internal class ToolChoiceFunction
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;
    }

    internal class Prediction
    {
        [JsonPropertyName("type")]
        public string Type { get; set; } = "content";
        [JsonPropertyName("content")]
        public string Content { get; set; } = string.Empty;
    }

    internal class DebugOptions
    {
        [JsonPropertyName("echo_upstream_body")]
        public bool? EchoUpstreamBody { get; set; } = null;
    }
}

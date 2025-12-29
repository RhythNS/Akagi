using System.Text.Json.Serialization;

namespace Akagi.LLMs.OpenRouter;

public class OpenRouterResponse
{
    [JsonPropertyName("id")]
    public required string Id { get; set; }

    [JsonPropertyName("choices")]
    public required List<Choice> Choices { get; set; }

    [JsonPropertyName("created")]
    public required int Created { get; set; }

    [JsonPropertyName("model")]
    public required string Model { get; set; }

    [JsonPropertyName("object")]
    public required string Object { get; set; }

    [JsonPropertyName("system_fingerprint")]
    public string? SystemFingerprint { get; set; }

    [JsonPropertyName("usage")]
    public ResponseUsage? Usage { get; set; }
}

public class Choice
{
    [JsonPropertyName("finish_reason")]
    public FinishReason? FinishReason { get; set; }

    [JsonPropertyName("error")]
    public ErrorResponse? Error { get; set; }

    [JsonPropertyName("native_finish_reason")]
    public string? NativeFinishReason { get; set; }

    // NonStreamingChoice property
    [JsonPropertyName("message")]
    public Message? Message { get; set; }

    // StreamingChoice property
    [JsonPropertyName("delta")]
    public Delta? Delta { get; set; }

    // NonChatChoice property
    [JsonPropertyName("text")]
    public string? Text { get; set; }

    // Helper properties to identify the choice type
    [JsonIgnore]
    public bool IsNonStreamingChoice => Message != null;

    [JsonIgnore]
    public bool IsStreamingChoice => Delta != null;

    [JsonIgnore]
    public bool IsNonChatChoice => Text != null;
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum FinishReason
{
    [JsonStringEnumMemberName("tool_calls")]
    ToolCalls,
    [JsonStringEnumMemberName("stop")]
    Stop,
    [JsonStringEnumMemberName("length")]
    Length,
    [JsonStringEnumMemberName("content_filter")]
    ContentFilter,
    [JsonStringEnumMemberName("error")]
    Error
}

public class Message
{
    [JsonPropertyName("content")]
    public string? Content { get; set; }

    [JsonPropertyName("role")]
    public required string Role { get; set; }

    [JsonPropertyName("tool_calls")]
    public List<ToolCall>? ToolCalls { get; set; }
}

public class Delta
{
    [JsonPropertyName("content")]
    public string? Content { get; set; }

    [JsonPropertyName("role")]
    public string? Role { get; set; }

    [JsonPropertyName("tool_calls")]
    public List<ToolCall>? ToolCalls { get; set; }
}

public class ToolCall
{
    [JsonPropertyName("id")]
    public required string Id { get; set; }

    [JsonPropertyName("type")]
    public required string Type { get; set; }

    [JsonPropertyName("function")]
    public required FunctionCall Function { get; set; }
}

public class FunctionCall
{
    [JsonPropertyName("name")]
    public required string Name { get; set; }

    [JsonPropertyName("arguments")]
    public string? Arguments { get; set; }
}

public class ResponseUsage
{
    [JsonPropertyName("prompt_tokens")]
    public int PromptTokens { get; set; }

    [JsonPropertyName("completion_tokens")]
    public int CompletionTokens { get; set; }

    [JsonPropertyName("total_tokens")]
    public int TotalTokens { get; set; }
}

public class ErrorResponse
{
    [JsonPropertyName("code")]
    public int Code { get; set; }

    [JsonPropertyName("message")]
    public required string Message { get; set; }

    [JsonPropertyName("metadata")]
    public Dictionary<string, object>? Metadata { get; set; }
}
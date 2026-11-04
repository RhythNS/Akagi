namespace Akagi.LLMs;

/// <summary>
/// What one request to a model used and cost, as the provider reported it.
/// </summary>
/// <param name="CachedTokens">Input tokens read from the provider's prompt cache, billed at a fraction of the input price.</param>
/// <param name="ReasoningTokens">Output tokens the model spent thinking; billed as output.</param>
/// <param name="Cost">In US dollars; null when the provider did not say.</param>
internal record LLMCallUsage(string Model, int PromptTokens, int CachedTokens, int CompletionTokens, int ReasoningTokens, double? Cost);

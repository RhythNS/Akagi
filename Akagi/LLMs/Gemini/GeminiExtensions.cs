namespace Akagi.LLMs.Gemini;

internal static class GeminiExtensions
{
    public static GeminiPayload.FunctionCallingMode ToFunctionCallingMode(this ILLM.RunMode runMode)
    {
        return runMode switch
        {
            ILLM.RunMode.TextOnly => GeminiPayload.FunctionCallingMode.NONE,
            ILLM.RunMode.CommandsOnly => GeminiPayload.FunctionCallingMode.ANY,
            ILLM.RunMode.Mixed => GeminiPayload.FunctionCallingMode.AUTO,
            _ => throw new ArgumentOutOfRangeException(nameof(runMode), runMode, null)
        };
    }
}

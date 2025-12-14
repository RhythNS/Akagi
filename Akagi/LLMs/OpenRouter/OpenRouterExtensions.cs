namespace Akagi.LLMs.OpenRouter;

internal static class OpenRouterExtensions
{
    public static OpenRouterPayload.ToolChoiceEnum ToToolChoiceEnum(this ILLM.RunMode runMode)
    {
        return runMode switch
        {
            ILLM.RunMode.Mixed => OpenRouterPayload.ToolChoiceEnum.Auto,
            ILLM.RunMode.TextOnly => OpenRouterPayload.ToolChoiceEnum.None,
            ILLM.RunMode.CommandsOnly => OpenRouterPayload.ToolChoiceEnum.Required,
            _ => throw new ArgumentOutOfRangeException(nameof(runMode), runMode, null)
        };
    }

    public static OpenRouterPayload.MessageRoleEnum ToMessageRoleEnum(this Characters.Conversations.Message.Type type)
    {
        return type switch
        {
            Characters.Conversations.Message.Type.User => OpenRouterPayload.MessageRoleEnum.User,
            Characters.Conversations.Message.Type.Character => OpenRouterPayload.MessageRoleEnum.Assistant,
            Characters.Conversations.Message.Type.System => OpenRouterPayload.MessageRoleEnum.System,
            _ => throw new ArgumentOutOfRangeException(nameof(type), type, null)
        };
    }
}

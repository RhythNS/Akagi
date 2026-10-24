using Akagi.Characters.Conversations;
using Akagi.Flow;
using Akagi.Receivers;
using Microsoft.Extensions.Logging;

namespace Akagi.Characters.CharacterBehaviors.MessageCompilers;

internal class AddTimeStampCompiler : MessageCompiler
{
    private string format = string.Empty;

    public string Format
    {
        get => format;
        set => SetProperty(ref format, value);
    }

    public override Task<List<Conversation>> FilterCompileAsync(Context context, List<Conversation> filteredConversations)
    {
        try
        {
            DateTime.Now.ToString(Format);
        }
        catch (Exception ex)
        {
            Globals.Instance.GetLogger<AddTimeStampCompiler>().LogWarning(ex, "Invalid date time format string: {Format}", Format);
            return Task.FromResult(filteredConversations);
        }

        foreach (Conversation conversation in filteredConversations)
        {
            foreach (Message message in conversation.Messages)
            {
                switch (message)
                {
                    case TextMessage textMessage:
                        textMessage.Text = $"[{message.Time.ToString(Format)}] {textMessage.Text}";
                        break;

                    default:
                        break;
                }
            }
        }

        return Task.FromResult(filteredConversations);
    }
}

using Akagi.Bridge.Attributes;
using Akagi.Characters.Conversations;
using Akagi.Flow;
using Akagi.Life;
using Akagi.Receivers;
using Microsoft.Extensions.Logging;

namespace Akagi.Characters.CharacterBehaviors.MessageCompilers;

/// <summary>
/// Puts the time in front of messages so the model knows when they were sent. The character's own replies are left
/// unstamped unless <see cref="StampOwnMessages"/> is set: models copy whatever their earlier replies start with, and
/// a stamped reply teaches them to write "[Sun 14:43]" themselves.
/// </summary>
internal class AddTimeStampCompiler : MessageCompiler
{
    private string format = string.Empty;
    private bool stampOwnMessages;

    public string Format
    {
        get => format;
        set => SetProperty(ref format, value);
    }

    [NodeDocu("Also stamp the character's own replies. Off by default: the model starts copying the stamps into its replies.")]
    public bool StampOwnMessages
    {
        get => stampOwnMessages;
        set => SetProperty(ref stampOwnMessages, value);
    }

    public override async Task<List<Conversation>> FilterCompileAsync(Context context, List<Conversation> filteredConversations)
    {
        try
        {
            DateTime.Now.ToString(Format);
        }
        catch (Exception ex)
        {
            Globals.Instance.GetLogger<AddTimeStampCompiler>().LogWarning(ex, "Invalid date time format string: {Format}", Format);
            return filteredConversations;
        }

        TimeZoneInfo zone = await ZoneAsync(context);
        string? self = context?.Character?.Id;
        foreach (Conversation conversation in filteredConversations)
        {
            foreach (Message message in conversation.Messages)
            {
                if (message is not TextMessage textMessage)
                {
                    continue;
                }
                // Stamps a reply copied before this was fixed come off, so the history stops showing them
                string text = MessageStamps.StripLeading(textMessage.Text);
                bool own = message.From == Message.Type.Character && (self == null || message.CharacterId == null || message.CharacterId == self);
                textMessage.Text = own && !StampOwnMessages ? text : $"{Stamp(LocalTime(message.Time, zone), Format)} {text}";
            }
        }

        return filteredConversations;
    }

    /// <summary>
    /// The user's time zone from their life profile, the same one the life context shows the time in; the machine's
    /// when they have none. Messages are stored in UTC, and stamping that made every message an hour or two early.
    /// </summary>
    private static async Task<TimeZoneInfo> ZoneAsync(Context? context)
    {
        if (context?.User?.Id is not string userId || string.IsNullOrEmpty(userId))
        {
            return TimeZoneInfo.Local;
        }
        LifeProfile? profile = await context.DatabaseFactory.GetDatabase<ILifeProfileDatabase>().GetForUser(userId);
        return string.IsNullOrWhiteSpace(profile?.TimeZoneId) ? TimeZoneInfo.Local : LifeClock.FindTimeZone(profile.TimeZoneId);
    }

    internal static DateTime LocalTime(DateTime time, TimeZoneInfo zone) =>
        time.Kind == DateTimeKind.Local ? TimeZoneInfo.ConvertTime(time, zone) : LifeClock.ToLocal(time, zone);

    /// <summary>
    /// The time in brackets. A format that brings its own, such as "[ddd HH:mm]", is not wrapped a second time.
    /// </summary>
    internal static string Stamp(DateTime time, string format)
    {
        string formatted = time.ToString(format);
        return formatted.StartsWith('[') && formatted.EndsWith(']') ? formatted : $"[{formatted}]";
    }
}

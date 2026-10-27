using Akagi.Life;
using System.Globalization;

namespace Akagi.Receivers.Commands.Life;

/// <summary>
/// Base for tools that read or update the user's <see cref="LifeProfile"/>.
/// </summary>
internal abstract class LifeCommand : Command
{
    public override bool ContinueAfterExecution => true;

    protected static Task<LifeProfile> LoadProfile(Context context)
    {
        string userId = context.User.Id ?? throw new InvalidOperationException("User has no id.");
        return context.DatabaseFactory.GetDatabase<ILifeProfileDatabase>().GetOrCreate(userId);
    }

    protected static Task SaveProfile(Context context, LifeProfile profile)
    {
        return context.DatabaseFactory.GetDatabase<ILifeProfileDatabase>().SaveDocumentAsync(profile);
    }

    protected Command[] Reply(Context context, string output)
    {
        context.Conversation.AddMessage(CreateCommandMessage(output));
        return [];
    }

    protected static Argument Text(string name, string description, bool required = false) => new()
    {
        Name = name,
        Description = description,
        IsRequired = required,
        ArgumentType = Argument.Type.String
    };

    protected static Argument Integer(string name, string description, bool required = false) => new()
    {
        Name = name,
        Description = description,
        IsRequired = required,
        ArgumentType = Argument.Type.Int
    };

    protected static Argument Number(string name, string description, bool required = false) => new()
    {
        Name = name,
        Description = description,
        IsRequired = required,
        ArgumentType = Argument.Type.Float
    };

    protected static Argument Flag(string name, string description, bool required = false) => new()
    {
        Name = name,
        Description = description,
        IsRequired = required,
        ArgumentType = Argument.Type.Bool
    };

    /// <summary>
    /// Parses a user local "yyyy-MM-dd HH:mm", "yyyy-MM-dd" or "HH:mm" (today) into UTC.
    /// </summary>
    protected static DateTime? ParseLocalTime(LifeProfile profile, string? text, DateTime nowUtc)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        string[] formats = ["yyyy-MM-dd HH:mm", "yyyy-MM-ddTHH:mm", "yyyy-MM-dd H:mm", "yyyy-MM-dd"];
        if (DateTime.TryParseExact(text.Trim(), formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime local))
        {
            return LifeClock.ToUtc(local, profile.TimeZone);
        }
        if (LifeClock.TryParseTimeOfDay(text, out int minutes))
        {
            DateTime today = profile.ToLocal(nowUtc).Date;
            return LifeClock.ToUtc(today.AddMinutes(minutes), profile.TimeZone);
        }
        return null;
    }
}

using Akagi.Life;
using Akagi.Life.Health;
using System.Globalization;
using System.Text;

namespace Akagi.Communication.Commands.Life;

internal class UploadHealthCommand : DocumentCommand
{
    public override string Name => "/uploadHealth";

    public override string Description =>
        "Imports sleep, steps and resting heart rate from a Health Connect style JSON export. Usage: /uploadHealth <json file>";

    public override async Task<CommandResult> ExecuteAsync(Context context, Document[] documents, string[] args)
    {
        if (documents.Length == 0)
        {
            await Communicator.SendMessage(context.User, "Please attach a JSON health export.");
            return CommandResult.Fail("No documents provided.");
        }

        ILifeProfileDatabase database = context.DatabaseFactory.GetDatabase<ILifeProfileDatabase>();
        LifeProfile profile = await database.GetOrCreate(context.User.Id!);

        StringBuilder response = new();
        bool imported = false;
        foreach (Document document in documents)
        {
            using MemoryStream? stream = await document.GetStream();
            if (stream == null)
            {
                response.AppendLine($"{document.Name}: could not be read.");
                continue;
            }

            try
            {
                stream.Position = 0;
                using StreamReader reader = new(stream, Encoding.UTF8);
                HealthImport.ImportResult result = HealthImport.Import(profile, await reader.ReadToEndAsync(), document.Name);
                response.AppendLine($"{document.Name}: {result}");
                imported |= result.TotalDays > 0;
            }
            catch (Exception ex)
            {
                response.AppendLine($"{document.Name}: {ex.Message}");
            }
        }

        if (imported)
        {
            await database.SaveDocumentAsync(profile);
            HealthSummary summary = HealthSummary.Create(profile, DateTime.UtcNow);
            if (summary.HasData)
            {
                response.AppendLine().AppendLine(summary.Describe());
            }
        }

        await Communicator.SendMessage(context.User, response.ToString().TrimEnd());
        return imported ? CommandResult.Ok : CommandResult.Fail("Nothing imported.");
    }
}

internal class TimeZoneCommand : TextCommand
{
    public override string Name => "/timezone";

    public override string Description => "Shows or sets your time zone, used for greetings, days and quiet hours. Usage: /timezone [Europe/Berlin]";

    public override async Task<CommandResult> ExecuteAsync(Context context, string[] args)
    {
        ILifeProfileDatabase database = context.DatabaseFactory.GetDatabase<ILifeProfileDatabase>();
        LifeProfile profile = await database.GetOrCreate(context.User.Id!);

        if (args.Length == 0)
        {
            string zone = string.IsNullOrWhiteSpace(profile.TimeZoneId) ? "UTC (not set)" : profile.TimeZoneId;
            await Communicator.SendMessage(context.User, $"Time zone: {zone}. Local time: {profile.ToLocal(DateTime.UtcNow).ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)}");
            return CommandResult.Ok;
        }

        if (!LifeClock.IsValidTimeZone(args[0]))
        {
            await Communicator.SendMessage(context.User, $"Unknown time zone '{args[0]}'. Use an IANA id such as Europe/Berlin or Asia/Tokyo.");
            return CommandResult.Fail("Unknown time zone.");
        }

        profile.TimeZoneId = args[0];
        await database.SaveDocumentAsync(profile);
        await Communicator.SendMessage(context.User, $"Time zone set to {args[0]}. Local time: {profile.ToLocal(DateTime.UtcNow).ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)}");
        return CommandResult.Ok;
    }
}

internal class LifeStatusCommand : TextCommand
{
    public override string Name => "/life";

    public override string Description => "Shows what your companion currently knows about your mood, worries, sleep, work and study.";

    public override async Task<CommandResult> ExecuteAsync(Context context, string[] args)
    {
        LifeProfile? profile = await context.DatabaseFactory.GetDatabase<ILifeProfileDatabase>().GetForUser(context.User.Id!);
        string text = profile == null ? string.Empty : LifeContextRenderer.Render(profile, DateTime.UtcNow, LifeSections.All);
        await Communicator.SendMessage(context.User, string.IsNullOrWhiteSpace(text) ? "Nothing recorded yet." : text);
        return CommandResult.Ok;
    }
}

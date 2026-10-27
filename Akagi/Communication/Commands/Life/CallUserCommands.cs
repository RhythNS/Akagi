using Akagi.Characters;
using Akagi.Flow;
using Akagi.Life.Japanese.Calls;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using System.Text;

namespace Akagi.Communication.Commands.Life;

internal class AnalyzeCallCommand : DocumentCommand
{
    public override string Name => "/analyzeCall";

    public override string Description =>
        "Queues a recorded Japanese conversation (audio or SRT/VTT/TXT transcript) for diagnosis. " +
        "Usage: /analyzeCall [partner] [casual|polite|formal] [left|right] [speaker=Me]. " +
        "left/right picks your channel in a stereo recording; speaker= names your label in a transcript. " +
        "Telegram only passes files up to 20 MB, so put long recordings in the call inbox folder.";

    public override async Task<CommandResult> ExecuteAsync(Context context, Document[] documents, string[] args)
    {
        if (documents.Length == 0)
        {
            await Communicator.SendMessage(context.User, "Please attach a recording or transcript.");
            return CommandResult.Fail("No documents provided.");
        }

        CallAnalysisOptions options = Globals.Instance.ServiceProvider.GetRequiredService<IOptionsMonitor<CallAnalysisOptions>>().CurrentValue;
        ICallJobDatabase jobs = context.DatabaseFactory.GetDatabase<ICallJobDatabase>();
        (string Analysis, string Delivery) characters = await CallJobs.ResolveCharactersAsync(
            context.DatabaseFactory.GetDatabase<ICharacterDatabase>(), context.User, options, context.Character);
        CallRequest request = CallRequest.Parse(args);

        StringBuilder response = new();
        foreach (Document document in documents)
        {
            if (!CallJobs.IsSupported(document.Name))
            {
                response.AppendLine($"{document.Name}: unsupported file type.");
                continue;
            }

            using MemoryStream? stream = await document.GetStream();
            if (stream == null)
            {
                response.AppendLine($"{document.Name}: could not be read.");
                continue;
            }
            stream.Position = 0;
            (_, DateTime? recordedAt) = CallJobs.ParseFileName(document.Name);
            CallJob job = await CallJobs.CreateAsync(jobs, options, context.User.Id!, stream, document.Name, request, recordedAt, characters);
            response.AppendLine($"{document.Name}: queued as [{job.ShortId}]. You will hear back when the analysis is done.");
        }

        await Communicator.SendMessage(context.User, response.ToString().TrimEnd());
        return CommandResult.Ok;
    }
}

internal class CallsCommand : TextCommand
{
    public override string Name => "/calls";

    public override string Description => "Lists your analyzed calls, or shows one report. Usage: /calls [id]";

    public override async Task<CommandResult> ExecuteAsync(Context context, string[] args)
    {
        List<CallJob> jobs = [.. (await context.DatabaseFactory.GetDatabase<ICallJobDatabase>().GetForUser(context.User.Id!))
            .OrderByDescending(j => j.CreatedAt)];

        if (args.Length > 0)
        {
            CallJob? job = jobs.FirstOrDefault(j => string.Equals(j.ShortId, args[0].Trim('[', ']'), StringComparison.OrdinalIgnoreCase));
            string text = job == null
                ? $"No call with id '{args[0]}'."
                : job.Status == CallJobStatus.Completed ? job.Report : CallAnalysisPipeline.DescribeProgress(job);
            await Communicator.SendMessage(context.User, text);
            return CommandResult.Ok;
        }

        await Communicator.SendMessage(context.User, jobs.Count == 0
            ? "No calls yet. Send one with /analyzeCall or drop it into the call inbox."
            : string.Join(Environment.NewLine, jobs.Take(15).Select(CallAnalysisPipeline.DescribeProgress)));
        return CommandResult.Ok;
    }
}

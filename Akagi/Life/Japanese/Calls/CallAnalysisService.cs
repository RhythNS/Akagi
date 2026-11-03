using Akagi.Characters;
using Akagi.Characters.Conversations;
using Akagi.Characters.Groups;
using Akagi.Data;
using Akagi.LLMs;
using Akagi.Receivers.Commands;
using Akagi.Users;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Akagi.Life.Japanese.Calls;

/// <summary>
/// Picks up recordings from the inbox folder and works through pending call jobs one at a time.
/// </summary>
internal class CallAnalysisService : BackgroundService
{
    /// <summary>
    /// Inbox files younger than this may still be copying.
    /// </summary>
    public static readonly TimeSpan SettleTime = TimeSpan.FromMinutes(1);

    private readonly IServiceProvider _services;
    private readonly IOptionsMonitor<CallAnalysisOptions> _options;
    private readonly ILogger<CallAnalysisService> _logger;

    public CallAnalysisService(IServiceProvider services, IOptionsMonitor<CallAnalysisOptions> options, ILogger<CallAnalysisService> logger)
    {
        _services = services;
        _options = options;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(Math.Max(5, _options.CurrentValue.PollSeconds)), stoppingToken);
                await ScanInboxAsync(stoppingToken);
                await ProcessNextAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                // Also an OperationCanceledException that is not the shutdown, such as an HTTP timeout: the loop goes on
                _logger.LogError(ex, "Call analysis tick failed");
            }
        }
    }

    private async Task ProcessNextAsync(CancellationToken cancellationToken)
    {
        ICallJobDatabase jobs = _services.GetRequiredService<ICallJobDatabase>();
        CallJob? job = await jobs.GetNextPending();
        if (job == null)
        {
            return;
        }

        CallAnalysisPipeline pipeline = CreatePipeline();
        try
        {
            await pipeline.RunAsync(job, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // A timed out request is a TaskCanceledException; it is a failed attempt, not the shutdown
            _logger.LogError(ex, "Call {JobId} failed (attempt {Attempt})", job.Id, job.Attempts + 1);
            await pipeline.FailAttemptAsync(job, ex);
        }
    }

    public CallAnalysisPipeline CreatePipeline()
    {
        CallAnalysisOptions options = _options.CurrentValue;
        IDatabaseFactory databaseFactory = _services.GetRequiredService<IDatabaseFactory>();
        ToolCallChunkAnalyzer analyzer = new(databaseFactory, _services.GetRequiredService<ILLMFactory>(),
            _services.GetRequiredService<ICommandFactory>(), _logger);

        return new CallAnalysisPipeline(
            _services.GetRequiredService<ICallJobDatabase>(),
            _services.GetRequiredService<IJapaneseDiagnosisDatabase>(),
            new AudioSplitter(options.FfmpegPath),
            _services.GetRequiredService<ITranscriber>(),
            analyzer,
            DeliverAsync,
            options,
            _logger);
    }

    private async Task DeliverAsync(CallJob job)
    {
        Character? character = await _services.GetRequiredService<ICharacterDatabase>().GetCharacter(job.DeliveryCharacterId);
        User? user = await _services.GetRequiredService<IUserDatabase>().GetDocumentByIdAsync(job.UserId);
        if (character == null || user == null)
        {
            return;
        }

        TextMessage message = new()
        {
            From = Message.Type.System,
            Time = DateTime.UtcNow,
            Text = CallJobs.BuildDeliveryInstruction(job),
            CharacterId = null
        };
        await _services.GetRequiredService<IProactiveDelivery>().DeliverAsync(character, user, message);
    }

    /// <summary>
    /// Inbox/&lt;username&gt;/&lt;file&gt;: each settled file becomes a job and is moved into storage.
    /// </summary>
    private async Task ScanInboxAsync(CancellationToken cancellationToken)
    {
        CallAnalysisOptions options = _options.CurrentValue;
        if (string.IsNullOrWhiteSpace(options.InboxDirectory) || !Directory.Exists(options.InboxDirectory))
        {
            return;
        }

        IUserDatabase users = _services.GetRequiredService<IUserDatabase>();
        foreach (string userFolder in Directory.GetDirectories(options.InboxDirectory))
        {
            User? user = await users.GetByUsername(Path.GetFileName(userFolder));
            if (user?.Id == null)
            {
                continue;
            }

            foreach (string file in Directory.GetFiles(userFolder).Where(CallJobs.IsSupported))
            {
                if (DateTime.UtcNow - File.GetLastWriteTimeUtc(file) < SettleTime)
                {
                    continue;
                }
                await ImportAsync(options, user, file, cancellationToken);
            }
        }
    }

    private async Task ImportAsync(CallAnalysisOptions options, User user, string file, CancellationToken cancellationToken)
    {
        (CallRequest request, DateTime? recordedAt) = CallJobs.ParseFileName(file);
        (string Analysis, string Delivery) characters = await CallJobs.ResolveCharactersAsync(
            _services.GetRequiredService<ICharacterDatabase>(), user, options, fallback: null);

        CallJob job;
        await using (FileStream stream = File.OpenRead(file))
        {
            job = await CallJobs.CreateAsync(_services.GetRequiredService<ICallJobDatabase>(), options, user.Id!, stream,
                Path.GetFileName(file), request, recordedAt, characters, cancellationToken);
        }
        File.Delete(file);
        _logger.LogInformation("Queued call {JobId} from inbox file {File}", job.Id, file);
    }
}

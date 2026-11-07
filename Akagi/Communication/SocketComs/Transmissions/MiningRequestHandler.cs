using Akagi.Bridge.Chat.Models;
using Akagi.Bridge.Chat.Transmissions;
using Akagi.Bridge.Chat.Transmissions.Requests;
using Akagi.Bridge.Chat.Transmissions.Responses;
using Akagi.Life.Japanese.Calls;
using Akagi.Life.Japanese.Mining;
using Akagi.Users;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Akagi.Communication.SocketComs.Transmissions;

/// <summary>
/// Mining a sentence, following its clip being cut, and its downloads; also a whole call's subtitles.
/// </summary>
internal class MiningRequestHandler : SocketTransmissionHandler
{
    public override string HandlesType => nameof(MiningRequestTransmission);

    private readonly MiningService _mining;
    private readonly ILogger<MiningRequestHandler> _logger;

    public MiningRequestHandler(ICallJobDatabase jobs, IMinedSentenceDatabase mined, IOptions<CallAnalysisOptions> options, ILogger<MiningRequestHandler> logger)
        : this(new MiningService(jobs, mined, new AudioClipper(options.Value.FfmpegPath), options.Value.StorageDirectory, logger), logger)
    {
    }

    internal MiningRequestHandler(MiningService mining, ILogger<MiningRequestHandler> logger)
    {
        _mining = mining;
        _logger = logger;
    }

    public override Task ExecuteAsync(Context context, TransmissionWrapper transmissionWrapper)
    {
        User user = context.User ?? throw new ArgumentNullException(nameof(context), "User cannot be null in MiningRequestHandler");
        MiningRequestTransmission request = GetTransmission<MiningRequestTransmission>(transmissionWrapper);
        SocketSession session = context.Session;

        // A whole call's audio takes a while, and the session reads nothing else until this returns
        _ = Task.Run(async () => session.SendTransmission(await ProcessAsync(user.Id!, request)));
        return Task.CompletedTask;
    }

    internal async Task<MiningResponseTransmission> ProcessAsync(string userId, MiningRequestTransmission request)
    {
        MiningResponseTransmission response = new() { RequestId = request.RequestId };
        try
        {
            switch (request.Action)
            {
                case MiningAction.List:
                    response.Sentences = [.. (await _mining.ListAsync(userId)).Select(View)];
                    break;

                case MiningAction.Add:
                {
                    (MinedSentence? sentence, string? error) = await _mining.AddAsync(userId, request.CallId, request.Time, request.Text, request.Target);
                    response.Error = error;
                    response.Sentence = sentence == null ? null : View(sentence);
                    break;
                }

                case MiningAction.Get:
                    Found(response, await _mining.GetAsync(userId, request.Id));
                    break;

                case MiningAction.Retry:
                    Found(response, await _mining.RetryAsync(userId, request.Id));
                    break;

                case MiningAction.Remove:
                    if (!await _mining.RemoveAsync(userId, request.Id))
                    {
                        response.Error = "That sentence was already removed.";
                    }
                    break;

                case MiningAction.Download:
                {
                    (MiningFile? file, string? error) = await _mining.DownloadAsync(userId, request.Id, request.File);
                    Attach(response, file, error);
                    break;
                }

                case MiningAction.ExportCall:
                {
                    MiningVoices voices = request.Voices == MiningVoiceChoice.Partner ? MiningVoices.Partner : MiningVoices.Both;
                    (MiningFile? file, string? error) = await _mining.ExportCallAsync(userId, request.CallId, voices, CancellationToken.None);
                    Attach(response, file, error);
                    break;
                }

                default:
                    response.Error = "Unknown mining request.";
                    break;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Mining request {Action} failed for user {UserId}", request.Action, userId);
            response.Error = "Something went wrong. Try again in a moment.";
        }
        return response;
    }

    private static void Found(MiningResponseTransmission response, MinedSentence? sentence)
    {
        response.Sentence = sentence == null ? null : View(sentence);
        if (sentence == null)
        {
            response.Error = "This sentence does not exist, or it was removed.";
        }
    }

    private static void Attach(MiningResponseTransmission response, MiningFile? file, string? error)
    {
        response.Error = error;
        if (file != null)
        {
            response.FileName = file.FileName;
            response.Data = file.Data;
        }
    }

    internal static MinedSentenceView View(MinedSentence sentence) => new()
    {
        Id = sentence.Id ?? string.Empty,
        CallId = sentence.CallId,
        CallName = sentence.CallName,
        Date = sentence.Date,
        Start = sentence.Start,
        End = sentence.End,
        Speaker = sentence.Speaker,
        IsLearner = sentence.IsLearner,
        Text = sentence.Text,
        Target = sentence.Target,
        AddedAt = sentence.AddedAt,
        State = sentence.State,
        Error = sentence.Error,
        ClipSeconds = sentence.ClipSeconds
    };
}

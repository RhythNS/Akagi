using Akagi.Bridge.Chat.Models;
using Akagi.Bridge.Chat.Transmissions;
using Akagi.Bridge.Chat.Transmissions.Requests;
using Akagi.Bridge.Chat.Transmissions.Responses;
using Akagi.Life.Japanese.Vocabulary;
using Akagi.Users;
using Microsoft.Extensions.Logging;

namespace Akagi.Communication.SocketComs.Transmissions;

/// <summary>
/// The vocabulary page: what it can show, one report under a filter, or one word.
/// </summary>
internal class VocabularyRequestHandler : SocketTransmissionHandler
{
    public override string HandlesType => nameof(VocabularyRequestTransmission);

    private readonly IVocabularyIndex _index;
    private readonly IWordDictionary _dictionary;
    private readonly VocabularyReport[] _reports;
    private readonly ILogger<VocabularyRequestHandler> _logger;

    public VocabularyRequestHandler(IVocabularyIndex index, IWordDictionary dictionary, IEnumerable<VocabularyReport> reports, ILogger<VocabularyRequestHandler> logger)
    {
        _index = index;
        _dictionary = dictionary;
        _reports = [.. reports
            .OrderBy(r => Array.IndexOf(VocabularyReport.GroupOrder, r.Group) is int group and >= 0 ? group : int.MaxValue)
            .ThenBy(r => r.Order)];
        _logger = logger;
    }

    public override Task ExecuteAsync(Context context, TransmissionWrapper transmissionWrapper)
    {
        User user = context.User ?? throw new ArgumentNullException(nameof(context), "User cannot be null in VocabularyRequestHandler");
        VocabularyRequestTransmission request = GetTransmission<VocabularyRequestTransmission>(transmissionWrapper);
        SocketSession session = context.Session;

        // Building a report over many calls takes a moment, and the session reads nothing else until this returns
        _ = Task.Run(async () => session.SendTransmission(await ProcessAsync(user.Id!, request)));
        return Task.CompletedTask;
    }

    internal async Task<VocabularyResponseTransmission> ProcessAsync(string userId, VocabularyRequestTransmission request)
    {
        VocabularyResponseTransmission response = new() { RequestId = request.RequestId };
        try
        {
            (List<CallTokens> calls, int mixed) = await _index.LoadAsync(userId);
            switch (request.Action)
            {
                case VocabularyAction.Catalog:
                    response.Catalog = new VocabCatalog
                    {
                        Reports = [.. _reports.Select(r => r.Info)],
                        Calls = [.. calls.Select(c => new VocabCallInfo { Id = c.CallId, Name = c.Name, Date = c.Date, Partner = c.Partner })],
                        Partners = [.. calls.Where(c => c.Partner.Length > 0).Select(c => c.Partner).Distinct(StringComparer.OrdinalIgnoreCase).Order()],
                        DictionaryReady = _dictionary.Ready,
                        MixedCalls = mixed
                    };
                    break;

                case VocabularyAction.Report:
                    VocabularyReport? report = _reports.FirstOrDefault(r => r.Key == request.ReportKey);
                    if (report == null)
                    {
                        response.Error = "Unknown report.";
                        break;
                    }
                    VocabularyView view = new(calls, request.Filter, _dictionary);
                    if (view.Calls.Count == 0)
                    {
                        response.Error = calls.Count == 0
                            ? "No finished call with your voice recorded separately yet."
                            : "No call matches the filter.";
                        break;
                    }
                    response.Report = report.Run(view, request.Options);
                    break;

                case VocabularyAction.Word:
                    response.Word = WordDetail.Build(new VocabularyView(calls, request.Filter, _dictionary), request.Lemma);
                    if (response.Word == null)
                    {
                        response.Error = $"{request.Lemma} was not said in the calls in view.";
                    }
                    break;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Vocabulary request {Action} {Report} failed for user {UserId}", request.Action, request.ReportKey, userId);
            response.Error = "Something went wrong. Try again in a moment.";
        }
        return response;
    }
}

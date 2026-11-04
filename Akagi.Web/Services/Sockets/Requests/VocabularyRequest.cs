using Akagi.Bridge.Chat.Transmissions.Requests;
using Akagi.Bridge.Chat.Transmissions.Responses;

namespace Akagi.Web.Services.Sockets.Requests;

/// <summary>
/// Asks the vocabulary engine for its catalog, a report or a word. Responses are matched by id.
/// </summary>
public class VocabularyRequest : Request<VocabularyResponseTransmission, VocabularyRequestTransmission>
{
    /// <summary>
    /// The first request after the bot starts tokenizes every call.
    /// </summary>
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(45);

    private readonly VocabularyRequestTransmission _transmission;

    public VocabularyRequest(VocabularyRequestTransmission transmission)
    {
        _transmission = transmission;
        _transmission.RequestId = Guid.NewGuid().ToString("N");
    }

    public string RequestId => _transmission.RequestId;

    public VocabularyRequestTransmission Transmission => _transmission;

    protected override VocabularyRequestTransmission GetTransmission()
    {
        return _transmission;
    }
}

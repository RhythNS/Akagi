using Akagi.Bridge.Chat.Transmissions.Requests;
using Akagi.Bridge.Chat.Transmissions.Responses;

namespace Akagi.Web.Services.Sockets.Requests;

/// <summary>
/// Reads or changes the user's model settings. Responses are matched by id.
/// </summary>
public class LLMSettingsRequest : Request<LLMSettingsResponseTransmission, LLMSettingsRequestTransmission>
{
    /// <summary>
    /// The overview may have to ask the providers for their models first.
    /// </summary>
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);

    private readonly LLMSettingsRequestTransmission _transmission;

    public LLMSettingsRequest(LLMSettingsRequestTransmission transmission)
    {
        _transmission = transmission;
        _transmission.RequestId = Guid.NewGuid().ToString("N");
    }

    public string RequestId => _transmission.RequestId;

    protected override LLMSettingsRequestTransmission GetTransmission()
    {
        return _transmission;
    }
}

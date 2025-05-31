using Akagi.Data;
using Akagi.LLMs;

namespace Akagi.Users;

internal class User : Savable
{
    private string _name = string.Empty;
    private string _username = string.Empty;
    private string _lastUsedCommunicator = string.Empty;
    private TelegramUser? _telegramUser;
    private bool _valid = false;
    private ILLM.LLMType _llmType;

    public string Name
    {
        get => _name;
        set => SetProperty(ref _name, value);
    }
    public string Username
    {
        get => _username;
        set => SetProperty(ref _username, value);
    }
    public string LastUsedCommunicator
    {
        get => _lastUsedCommunicator;
        set => SetProperty(ref _lastUsedCommunicator, value);
    }
    public TelegramUser? TelegramUser
    {
        get => _telegramUser;
        set => SetProperty(ref _telegramUser, value);
    }
    public bool Valid
    {
        get => _valid;
        set => SetProperty(ref _valid, value);
    }
    public ILLM.LLMType LLMType
    {
        get => _llmType;
        set => SetProperty(ref _llmType, value);
    }
}

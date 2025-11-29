using Akagi.Data;
using Akagi.LLMs;
using System.Text.Json;

namespace Akagi.Users;

internal class User : Savable
{
    private string _name = string.Empty;
    private string _username = string.Empty;
    private string _lastUsedCommunicator = string.Empty;
    private TelegramUser? _telegramUser;
    private GoogleUser? _googleUser;
    private bool _valid = false;
    private bool _admin = false;
    private LLMDefinition? _llmDefinition;
    private Dictionary<string, string> _configurations = [];

    public override bool Dirty
    {
        get => base.Dirty
            || (_telegramUser == null || _telegramUser.Dirty)
            || (_googleUser == null || _googleUser.Dirty);
        set
        {
            base.Dirty = value;
            if (value == false)
            {
                if (_telegramUser != null)
                {
                    _telegramUser.Dirty = false;
                }
            }
        }
    }

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
    public GoogleUser? GoogleUser
    {
        get => _googleUser;
        set => SetProperty(ref _googleUser, value);
    }
    public bool Valid
    {
        get => _valid;
        set => SetProperty(ref _valid, value);
    }
    public bool Admin
    {
        get => _admin;
        set => SetProperty(ref _admin, value);
    }
    public LLMDefinition? LLMDefinition
    {
        get => _llmDefinition;
        set => SetProperty(ref _llmDefinition, value);
    }
    public Dictionary<string, string> Configurations
    {
        get => _configurations;
        set => SetProperty(ref _configurations, value);
    }

    public T? GetConfig<T>() where T : class
    {
        string configKey = typeof(T).Name;
        if (!Configurations.TryGetValue(configKey, out string? value))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<T>(value);
        }
        catch (Exception ex)
        {
            throw new Exception($"Failed to get configuration for {configKey} from user {Name} ({Username})", ex);
        }
    }

    public T GetOrDefault<T>(T defaultValue) where T : class
    {
        T? value = GetConfig<T>();
        return value ?? defaultValue;
    }

    public void SetConfig<T>(T config) where T : class
    {
        string configKey = typeof(T).Name;
        try
        {
            string json = JsonSerializer.Serialize(config);
            Configurations[configKey] = json;
            Dirty = true;
        }
        catch (Exception ex)
        {
            throw new Exception($"Failed to set configuration for {configKey} for user {Name} ({Username})", ex);
        }
    }
}

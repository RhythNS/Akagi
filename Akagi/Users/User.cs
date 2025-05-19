using Akagi.Data;
using Akagi.LLMs;

namespace Akagi.Users;

internal class User : Savable
{
    public string Name { get; set; }
    public string Username { get; set; }
    public TelegramUser? TelegramUser { get; set; }
    public bool Valid { get; set; } = false;
    public ILLM.LLMType LLMType { get; set; }
}

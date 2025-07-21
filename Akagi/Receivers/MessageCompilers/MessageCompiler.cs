using Akagi.Characters;
using Akagi.Characters.Conversations;
using Akagi.Data;
using Akagi.Users;
using System.Text.Json.Serialization;

namespace Akagi.Receivers.MessageCompilers;

internal abstract class MessageCompiler : Savable
{
    private string _name = string.Empty;
    private string _description = string.Empty;

    public string Name
    {
        get => _name;
        set => SetProperty(ref _name, value);
    }
    public string Description
    {
        get => _description;
        set => SetProperty(ref _description, value);
    }
    [JsonIgnore]
    public Message.Type ReadableMessages { get; set; }

    public abstract Message[] Compile(User user, Character character);
}

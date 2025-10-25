using Akagi.Data;

namespace Akagi.Users;

internal class GoogleUser : DirtyTrackable
{
    private string _id = string.Empty;
    private string _email = string.Empty;

    public string Id
    {
        get => _id;
        set => SetProperty(ref _id, value);
    }
    public string Email
    {
        get => _email;
        set => SetProperty(ref _email, value);
    }
}

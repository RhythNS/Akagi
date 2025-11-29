using Akagi.Data;

namespace Akagi.Characters.Memories;

internal class Thought : DirtyTrackable
{
    private DateTime _timestamp = DateTime.UtcNow;

    public DateTime Timestamp
    {
        get => _timestamp;
        set => SetProperty(ref _timestamp, value);
    }
}

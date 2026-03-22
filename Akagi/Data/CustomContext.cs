namespace Akagi.Data;

internal class CustomContext : ContextBase
{
    public required Savable[] Track;
    protected override Savable?[] ToTrack => Track;
}

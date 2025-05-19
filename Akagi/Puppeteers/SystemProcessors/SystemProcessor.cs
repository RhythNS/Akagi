using Akagi.Data;

namespace Akagi.Puppeteers.SystemProcessors;

internal class SystemProcessor : Savable
{
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string SystemInstruction { get; set; } = string.Empty;
}

using System.Globalization;
using System.Reflection;

namespace Akagi.Utils;

internal class ApplicationInformation
{
    public DateTime StartTime { get; private init; } = DateTime.UtcNow;
    public TimeSpan Uptime => DateTime.UtcNow - StartTime;
    public DateTime? BuildTimestampUtc { get; private init; } = GetEmbeddedBuildTimestampUtc();

    private static DateTime? GetEmbeddedBuildTimestampUtc()
    {
        AssemblyMetadataAttribute? metadataAttribute =
            typeof(ApplicationInformation).Assembly
                                          .GetCustomAttributes<AssemblyMetadataAttribute>()
                                          .FirstOrDefault(attr => attr.Key == "BuildTimestampUtc");

        if (metadataAttribute?.Value != null)
        {
            if (DateTime.TryParse(metadataAttribute.Value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out DateTime timestamp))
            {
                return timestamp;
            }
        }
        return null;
    }
}

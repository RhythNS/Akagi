using System.Globalization;
using System.Reflection;

namespace Akagi.Utils;

internal class EmbeddedBuildTimeReader
{
    public static DateTime? GetEmbeddedBuildTimestampUtc(Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);

        AssemblyMetadataAttribute? metadataAttribute = assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
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

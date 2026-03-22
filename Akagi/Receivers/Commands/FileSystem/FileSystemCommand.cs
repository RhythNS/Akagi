using Microsoft.Extensions.Options;

namespace Akagi.Receivers.Commands;

internal abstract class FileSystemCommand : Command
{
    internal class Options
    {
        public string BasePath { get; set; } = string.Empty;
    }

    private readonly IOptionsMonitor<Options> _options;

    protected FileSystemCommand(IOptionsMonitor<Options> options)
    {
        _options = options;
    }

    protected bool TryResolvePath(string path, out string fullPath, out string error)
    {
        if (string.IsNullOrWhiteSpace(_options.CurrentValue.BasePath))
        {
            error = "Base path is not configured.";
            fullPath = string.Empty;
            return false;
        }

        string basePath = Path.GetFullPath(_options.CurrentValue.BasePath);
        fullPath = Path.GetFullPath(Path.Combine(basePath, path));

        if (fullPath != basePath &&
            !fullPath.StartsWith(basePath + Path.DirectorySeparatorChar))
        {
            error = $"Access denied: \"{path}\" resolves outside the allowed base directory.";
            fullPath = string.Empty;
            return false;
        }

        error = string.Empty;
        return true;
    }
}

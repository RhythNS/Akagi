using Akagi.Web.Models;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Primitives;

namespace Akagi.Web.Services.Sockets;

public class SocketFileProvider : IFileProvider
{
    private readonly ISocketService socketService;
    private readonly IMemoryCache _cache;
    private readonly ILogger<SocketFileProvider> _logger;
    private readonly IHttpContextAccessor _httpContextAccessor;

    private static readonly TimeSpan cacheDuration = TimeSpan.FromHours(2);

    public SocketFileProvider(
        ISocketService socketService,
        IMemoryCache cache,
        ILogger<SocketFileProvider> logger,
        IHttpContextAccessor httpContextAccessor)
    {
        this.socketService = socketService;
        _cache = cache;
        _logger = logger;
        _httpContextAccessor = httpContextAccessor;
    }

    public IDirectoryContents GetDirectoryContents(string subpath)
    {
        // TODO: Implement directory contents retrieval if needed
        return new NotFoundDirectoryContents();
    }

    public IFileInfo GetFileInfo(string subpath)
    {
        if (subpath.StartsWith('/'))
        {
            subpath = subpath.Substring(1);
        }

        HttpContext? httpContext = _httpContextAccessor.HttpContext;
        if (httpContext == null || !httpContext.User.Identity?.IsAuthenticated == true)
        {
            _logger.LogWarning("User not authenticated when accessing {Path}", subpath);
            return new NotFoundFileInfo(subpath);
        }

        System.Security.Claims.Claim? userIdClaim = httpContext.User.FindFirst("internal_id");
        if (userIdClaim == null)
        {
            _logger.LogWarning("User has no internal_id claim when accessing {Path}", subpath);
            return new NotFoundFileInfo(subpath);
        }

        string? circuitId = httpContext.Request.Headers["X-Circuit-Id"].FirstOrDefault() ??
                          httpContext.Request.Query["circuitId"].FirstOrDefault();

        if (string.IsNullOrEmpty(circuitId))
        {
            _logger.LogWarning("No circuit ID provided for user {UserId} when accessing {Path}", userIdClaim.Value, subpath);
            return new NotFoundFileInfo(subpath);
        }

        User user = new()
        {
            Id = userIdClaim.Value,
            Email = string.Empty,
            GoogleId = string.Empty,
            Name = string.Empty,
        };
        SocketClient? socketClient = socketService.Find(user, circuitId);

        if (socketClient == null)
        {
            _logger.LogWarning("Socket client not found for user {UserId} and circuit {CircuitId}.", user.Id, circuitId);
            return new NotFoundFileInfo(subpath);
        }

        return new SocketFileInfo(socketClient, subpath, _cache, cacheDuration);
    }

    public IChangeToken Watch(string filter)
    {
        return NullChangeToken.Singleton;
    }
}

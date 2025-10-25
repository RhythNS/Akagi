using Akagi.Web.Services.Sockets.Requests;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.FileProviders;

namespace Akagi.Web.Services.Sockets;

public class SocketFileInfo : IFileInfo
{
    private readonly SocketClient _socketClient;
    private readonly string _name;
    private readonly IMemoryCache _cache;
    private readonly TimeSpan _cacheDuration;

    private byte[]? _cachedContent;
    private bool _contentFetched = false;

    public bool Exists => true;
    public long Length
    {
        get
        {
            byte[]? cachedContent = GetCachedContent();
            if (cachedContent != null)
            {
                return cachedContent.Length;
            }

            using Stream stream = CreateReadStream();
            return _cachedContent?.Length ?? 0;
        }
    }
    public string PhysicalPath => null!; // Remote files don't have a physical path
    public string Name => _name;
    public DateTimeOffset LastModified => DateTimeOffset.UtcNow;
    public bool IsDirectory => false;

    public SocketFileInfo(SocketClient socketClient,
                          string name,
                          IMemoryCache cache,
                          TimeSpan cacheDuration)
    {
        _socketClient = socketClient;
        _name = name;
        _cache = cache;
        _cacheDuration = cacheDuration;
    }

    private byte[]? GetCachedContent()
    {
        if (_contentFetched)
        {
            return _cachedContent;
        }

        if (_cache != null && _cache.TryGetValue(_name, out byte[]? cachedData))
        {
            _cachedContent = cachedData!;
            _contentFetched = true;
            return _cachedContent;
        }

        return null;
    }

    public Stream CreateReadStream()
    {
        byte[]? content = GetCachedContent();

        if (content != null)
            return new MemoryStream(content);

        try
        {
            FileRequest imageRequest = new(_name);
            //TODO: remove the from days
            SocketFile file = imageRequest.Get(_socketClient, TimeSpan.FromDays(1)).GetAwaiter().GetResult();

            content = file.Data ?? throw new InvalidOperationException("File data is null");

            if (_cache != null && content.Length > 0)
            {
                MemoryCacheEntryOptions cacheEntryOptions = new MemoryCacheEntryOptions()
                    .SetSlidingExpiration(_cacheDuration)
                    .SetSize(content.Length);

                _cache.Set(_name, content, cacheEntryOptions);
            }

            _cachedContent = content;
            _contentFetched = true;

            return new MemoryStream(content);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error fetching remote file: {ex.Message}");
            return Stream.Null;
        }
    }
}

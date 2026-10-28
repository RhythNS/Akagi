using System.Collections.Concurrent;

namespace Akagi.Communication.TelegramComs;

/// <summary>
/// Telegram delivers the files of one album (several documents sent together) as separate messages that share a
/// media group id, and only the first carries the caption. This gathers them and hands the whole album over once no
/// new part arrived for <see cref="QuietPeriod"/>, so a document command sees every file at once.
/// </summary>
internal class AlbumCollector<T>
{
    private sealed class Album
    {
        public List<T> Parts { get; } = [];
        public int Version { get; set; }
        public bool Closed { get; set; }
    }

    private readonly ConcurrentDictionary<string, Album> _albums = new();
    private readonly Func<IReadOnlyList<T>, Task> _onComplete;

    public AlbumCollector(TimeSpan quietPeriod, Func<IReadOnlyList<T>, Task> onComplete)
    {
        QuietPeriod = quietPeriod;
        _onComplete = onComplete;
    }

    public TimeSpan QuietPeriod { get; }

    public int Pending => _albums.Count;

    /// <summary>
    /// Adds a part and returns a task that completes when this call's wait is over (flushed or superseded).
    /// Callers usually do not await it; tests do.
    /// </summary>
    public Task Add(string albumId, T part)
    {
        while (true)
        {
            Album album = _albums.GetOrAdd(albumId, _ => new Album());
            lock (album)
            {
                // The album flushed between lookup and lock; a late part starts a new one
                if (album.Closed)
                {
                    continue;
                }
                album.Parts.Add(part);
                return FlushWhenQuiet(albumId, album, ++album.Version);
            }
        }
    }

    private async Task FlushWhenQuiet(string albumId, Album album, int version)
    {
        await Task.Delay(QuietPeriod);
        List<T> parts;
        lock (album)
        {
            // A newer part restarted the wait
            if (album.Version != version || !_albums.TryRemove(new KeyValuePair<string, Album>(albumId, album)))
            {
                return;
            }
            album.Closed = true;
            parts = [.. album.Parts];
        }
        await _onComplete(parts);
    }
}

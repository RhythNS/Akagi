using Akagi.Data;
using Microsoft.Extensions.Options;
using MongoDB.Bson;

namespace Akagi.Characters.VoiceClips;

internal interface IVoiceClipsDatabase : IDatabase<VoiceClip>
{
    public Task SaveFileAsync(VoiceClip voiceClip, Stream stream, string? fileName = null);
    public Task<Stream> LoadFileAsync(VoiceClip voiceClip);
}

internal class VoiceClipsDatabase : Database<VoiceClip>, IVoiceClipsDatabase
{
    public override string CollectionName => "voice_clips";

    private readonly IFileDatabase _fileDatabase;

    public VoiceClipsDatabase(IFileDatabase fileDatabase, IOptionsMonitor<DatabaseOptions> options)
        : base(options)
    {
        _fileDatabase = fileDatabase;
    }

    public override bool CanSave(Savable savable) => savable is VoiceClip;

    public override Task SaveAsync(Savable savable) => SaveDocumentAsync((VoiceClip)savable);

    public async Task SaveFileAsync(VoiceClip voiceClip, Stream stream, string? fileName = null)
    {
        if (string.IsNullOrEmpty(fileName))
        {
            fileName = $"{Guid.NewGuid()}.audio";
        }

        ObjectId fileId = await _fileDatabase.UploadFileAsync(stream, fileName, "audio");
        voiceClip.AudioId = fileId.ToString();
        await SaveAsync(voiceClip);
    }

    public Task<Stream> LoadFileAsync(VoiceClip voiceClip)
    {
        return _fileDatabase.DownloadFileAsync(ObjectId.Parse(voiceClip.AudioId));
    }
}

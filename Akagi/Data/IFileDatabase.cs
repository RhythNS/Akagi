using MongoDB.Bson;

namespace Akagi.Data;

internal interface IFileDatabase
{
    public Task<ObjectId> UploadFileAsync(Stream fileStream, string fileName, string contentType);
    
    public Task<Stream> DownloadFileAsync(ObjectId id);
    
    public Task DeleteFileAsync(ObjectId id);
}

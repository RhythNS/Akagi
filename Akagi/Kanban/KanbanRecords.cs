using Akagi.Bridge.Chat.Models;
using Akagi.Data;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Driver;

namespace Akagi.Kanban;

internal class KanbanColumn
{
    public string Id { get; set; } = NewId();
    public string Name { get; set; } = string.Empty;
    public KanbanColumnKind Kind { get; set; }

    public static string NewId() => Guid.NewGuid().ToString("N")[..8];
}

/// <summary>
/// A project of the user: its columns from left to right and how its plan is made. Tickets are documents of their own.
/// </summary>
[BsonIgnoreExtraElements]
internal class KanbanProject : Savable
{
    [BsonRepresentation(BsonType.ObjectId)]
    public string UserId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    /// <summary>Markdown knowledge about the project, injected into characters.</summary>
    public string Description { get; set; } = string.Empty;
    public List<KanbanColumn> Columns { get; set; } = [];
    /// <summary>Midnight UTC of the first planned day, or null for today.</summary>
    public DateTime? StartDate { get; set; }
    public int WorkDays { get; set; } = KanbanProjectView.WeekDays;
    public int Parallel { get; set; } = 1;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public bool Archived { get; set; }
    /// <summary>The number the next ticket gets.</summary>
    public int NextNumber { get; set; } = 1;

    public KanbanColumn? Column(string? id) => Columns.FirstOrDefault(c => c.Id == id);

    public KanbanColumnKind KindOf(string? columnId) => Column(columnId)?.Kind ?? KanbanColumnKind.Todo;

    public KanbanColumn FirstOf(KanbanColumnKind kind) =>
        Columns.FirstOrDefault(c => c.Kind == kind) ?? Columns.First();
}

internal class KanbanUpdate
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..10];
    public DateTime At { get; set; } = DateTime.UtcNow;
    public string Author { get; set; } = string.Empty;
    public string Text { get; set; } = string.Empty;
    public double? RemainingDays { get; set; }
}

[BsonIgnoreExtraElements]
internal class KanbanTicket : Savable
{
    [BsonRepresentation(BsonType.ObjectId)]
    public string UserId { get; set; } = string.Empty;
    [BsonRepresentation(BsonType.ObjectId)]
    public string ProjectId { get; set; } = string.Empty;
    public int Number { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string ColumnId { get; set; } = string.Empty;
    public double Rank { get; set; }
    public double RequiredDays { get; set; }
    public double RemainingDays { get; set; }
    /// <summary>Ids of the tickets this one waits for.</summary>
    public List<string> After { get; set; } = [];
    /// <summary>Midnight UTC of the first day it may be planned on.</summary>
    public DateTime? NotBefore { get; set; }
    public List<KanbanUpdate> Updates { get; set; } = [];
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? StartedAt { get; set; }
    public DateTime? DoneAt { get; set; }

    public string Reference => $"#{Number}";
}

internal interface IKanbanProjectDatabase : IDatabase<KanbanProject>
{
    public Task<List<KanbanProject>> GetForUser(string userId);
}

internal interface IKanbanTicketDatabase : IDatabase<KanbanTicket>
{
    public Task<List<KanbanTicket>> GetForProject(string projectId);
    public Task DeleteForProject(string projectId);
}

internal class KanbanProjectDatabase : Database<KanbanProject>, IKanbanProjectDatabase
{
    public override string CollectionName => "kanban_projects";

    public KanbanProjectDatabase(IOptionsMonitor<DatabaseOptions> options) : base(options)
    {
    }

    public override bool CanSave(Savable savable) => savable is KanbanProject;

    public override Task SaveAsync(Savable savable) => SaveDocumentAsync((KanbanProject)savable);

    public Task<List<KanbanProject>> GetForUser(string userId) =>
        GetDocumentsByPredicateAsync(Builders<KanbanProject>.Filter.Eq(p => p.UserId, userId));
}

internal class KanbanTicketDatabase : Database<KanbanTicket>, IKanbanTicketDatabase
{
    public override string CollectionName => "kanban_tickets";

    public KanbanTicketDatabase(IOptionsMonitor<DatabaseOptions> options) : base(options)
    {
    }

    public override bool CanSave(Savable savable) => savable is KanbanTicket;

    public override Task SaveAsync(Savable savable) => SaveDocumentAsync((KanbanTicket)savable);

    public Task<List<KanbanTicket>> GetForProject(string projectId) =>
        GetDocumentsByPredicateAsync(Builders<KanbanTicket>.Filter.Eq(t => t.ProjectId, projectId));

    public Task DeleteForProject(string projectId) =>
        GetCollection().DeleteManyAsync(Builders<KanbanTicket>.Filter.Eq(t => t.ProjectId, projectId));
}

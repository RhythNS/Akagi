
using Akagi.Data;
using Akagi.Flow;
using Akagi.Scheduling;
using Akagi.Scheduling.Tasks;
using Akagi.Users;
using Microsoft.Extensions.DependencyInjection;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace Akagi.Characters.TriggerPoints.Actions;

internal class TriggerDelay : TriggerAction
{
    public class TriggerDeferContext
    {
        public required string TriggerActionId { get; init; }
        public required string CharacterId { get; init; }
        public required string UserId { get; init; }
    }

    public class TriggerDelayHandler : DeferTask.IDeferHandler
    {
        public async Task OnDeferAsync(object? data)
        {
            if (data is not TriggerDeferContext deferContext)
            {
                throw new InvalidOperationException("Invalid defer context data.");
            }

            IDatabaseFactory databaseFactory = Globals.Instance.ServiceProvider.GetRequiredService<IDatabaseFactory>();

            Character character = await databaseFactory
                .GetDatabase<ICharacterDatabase>()
                .GetCharacter(deferContext.CharacterId)
                ?? throw new InvalidOperationException("Character not found.");

            User user = await databaseFactory
                .GetDatabase<IUserDatabase>()
                .GetUser(deferContext.UserId)
                ?? throw new InvalidOperationException("User not found.");

            TriggerPoint.TriggerContext context = new()
            {
                Character = character,
                User = user,
                DatabaseFactory = databaseFactory,
            };

            TriggerAction triggerAction = await databaseFactory
                .GetDatabase<ITriggerActionDatabase>()
                .GetDocumentByIdAsync(deferContext.TriggerActionId)
                ?? throw new InvalidOperationException("Trigger action not found.");

            await triggerAction.Init(context);
            await triggerAction.ExecuteAsync();

            await databaseFactory.SaveIfDirty(triggerAction);
        }
    }

    private int _minutes = 0;
    private string _triggerActionId = string.Empty;
    private string? _deferTaskId = string.Empty;

    public int Minutes
    {
        get => _minutes;
        set => SetProperty(ref _minutes, value);
    }
    [BsonRepresentation(BsonType.ObjectId)]
    public string TriggerActionId
    {
        get => _triggerActionId;
        set => SetProperty(ref _triggerActionId, value);
    }
    [BsonRepresentation(BsonType.ObjectId)]
    public string? DeferTaskId
    {
        get => _deferTaskId;
        set => SetProperty(ref _deferTaskId, value);
    }

    public override async Task ExecuteAsync()
    {
        ITaskDatabase taskDatabase = Globals.Instance.ServiceProvider.GetRequiredService<ITaskDatabase>();

        if (string.IsNullOrEmpty(DeferTaskId) == false)
        {
            await taskDatabase.DeleteDocumentByIdAsync(DeferTaskId!);
        }
        DeferTask deferTask = new()
        {
            HandlerType = typeof(TriggerDelayHandler),
            Time = DateTime.UtcNow.AddMinutes(Minutes),
            Data = new TriggerDeferContext()
            {
                TriggerActionId = Id!,
                CharacterId = Context.Character.Id!,
                UserId = Context.User.Id!,
            }
        };
        await taskDatabase.SaveDocumentAsync(deferTask);
    }
}

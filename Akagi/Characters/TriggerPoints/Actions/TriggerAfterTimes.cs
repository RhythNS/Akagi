
using MongoDB.Bson.Serialization.Attributes;

namespace Akagi.Characters.TriggerPoints.Actions;

internal class TriggerAfterTimes : TriggerAction
{
    private string _actionId = string.Empty;
    private int _times = 1;
    private int _currentTimes = 1;

    [BsonRepresentation(MongoDB.Bson.BsonType.ObjectId)]
    public string ActionId
    {
        get => _actionId;
        set => SetProperty(ref _actionId, value);
    }
    public int Times
    {
        get => _times;
        set => SetProperty(ref _times, value);
    }
    public int CurrentTimes
    {
        get => _currentTimes;
        set => SetProperty(ref _currentTimes, value);
    }

    private TriggerAction? action = null;

    public override bool Dirty
    {
        get => base.Dirty || (action != null && action.Dirty);
        set
        {
            base.Dirty = value;
            if (action != null)
            {
                action.Dirty = value;
            }
        }
    }

    public override async Task Init(TriggerPoint.TriggerContext context)
    {
        await base.Init(context);

        action = await Context.DatabaseFactory.GetDatabase<ITriggerActionDatabase>()
            .GetDocumentByIdAsync(ActionId) ?? throw new Exception($"TriggerAction with ID {ActionId} not found");

        await action.Init(Context);
    }

    public override async Task ExecuteAsync()
    {
        if (CurrentTimes > 1)
        {
            CurrentTimes--;
            return;
        }

        await action!.ExecuteAsync();
        CurrentTimes = Times;
    }
}

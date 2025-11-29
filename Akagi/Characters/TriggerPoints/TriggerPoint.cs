using Akagi.Characters.TriggerPoints.Actions;
using Akagi.Data;
using Akagi.Users;

namespace Akagi.Characters.TriggerPoints;

internal class TriggerPoint : Savable
{
    public class TriggerContext : ContextBase
    {
        public required Character Character { get; init; }
        public required User User { get; init; }

        protected override Savable?[] ToTrack => [Character, User];
    }

    public enum TriggerType
    {
        MessageProcessed,
        ConversationEnded,
        ReflectionCompleted
    }

    public class TriggerActionEntry
    {
        public required string Id { get; init; }
        public required TriggerType OnTrigger { get; init; }
    }

    private string _name = string.Empty;
    private string _description = string.Empty;
    private TriggerActionEntry[] _triggerActions = [];
    private TriggerAction?[] _actions = [];

    public string Name
    {
        get => _name;
        set => SetProperty(ref _name, value);
    }
    public string Description
    {
        get => _description;
        set => SetProperty(ref _description, value);
    }
    public TriggerActionEntry[] TriggerActions
    {
        get => _triggerActions;
        set => SetProperty(ref _triggerActions, value);
    }

    protected TriggerContext Context
    {
        get
        {
            if (_context == null)
            {
                throw new InvalidOperationException("TriggerPoint has not been initialized with a context.");
            }
            return _context;
        }
    }
    protected Character Character => Context.Character;

    private TriggerContext? _context;

    public override bool Dirty
    {
        get => base.Dirty || _actions.Any(a => a != null && a.Dirty);
        set
        {
            base.Dirty = value;
            if (value == true)
            {
                return;
            }

            foreach (TriggerAction action in _actions.Where(x => x != null).Cast<TriggerAction>())
            {
                action.Dirty = false;
            }
        }
    }

    public virtual async Task Init(TriggerContext context)
    {
        _context = context;

        List<TriggerAction> actions = [];
        foreach (TriggerActionEntry entry in _triggerActions)
        {
            TriggerAction action = await context.DatabaseFactory
                .GetDatabase<TriggerActionDatabase>()
                .GetDocumentByIdAsync(entry.Id) ?? throw new Exception($"TriggerAction with ID {entry.Id} not found");

            await action.Init(context);

            actions.Add(action);
        }

        _actions = [.. actions];
    }

    public async virtual Task On(TriggerType type) => await TriggerAllTypes(type);

    public async virtual Task OnMessageProcessed() => await TriggerAllTypes(TriggerType.MessageProcessed);

    public virtual async Task OnConversationEnded() => await TriggerAllTypes(TriggerType.ConversationEnded);

    public virtual async Task OnReflectionCompleted() => await TriggerAllTypes(TriggerType.ReflectionCompleted);

    private async Task TriggerAllTypes(TriggerType type)
    {
        for (int i = 0; i < _triggerActions.Length; i++)
        {
            if (_triggerActions[i].OnTrigger == type)
            {
                await _actions[i]!.ExecuteAsync();
            }
        }
    }
}

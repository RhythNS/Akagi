using Akagi.Characters.Presets.Hardcoded.TriggerActions;
using Akagi.Characters.TriggerPoints;
using Akagi.Data;
using Akagi.Utils.Attributes;

namespace Akagi.Characters.Presets.Hardcoded.TriggerPoints;

[DependsOn(typeof(TriggerReflectPreset))]
internal class TriggerReflectOnCompletedConversationPreset : Preset
{
    private string _triggerId = string.Empty;

    public string TriggerId
    {
        get => _triggerId;
        set => SetProperty(ref _triggerId, value);
    }

    public override async Task CreateAsync(IDatabaseFactory databaseFactory)
    {
        TriggerReflectPreset reflect = await Load<TriggerReflectPreset>(databaseFactory);

        TriggerPoint triggerPoint = new()
        {
            Name = "Reflect on Completed Conversation",
            Description = "Triggers a reflection when a conversation is completed.",
            TriggerActions =
            [
                new TriggerPoint.TriggerActionEntry
                {
                    Id = reflect.TriggerId,
                    OnTrigger = TriggerPoint.TriggerType.ConversationEnded
                }
            ]
        };

        await Save(databaseFactory, triggerPoint);

        TriggerId = triggerPoint.Id!;
    }
}

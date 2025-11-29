using Akagi.Flow;
using Akagi.Receivers;
using Microsoft.Extensions.DependencyInjection;

namespace Akagi.Characters.TriggerPoints.Actions;

internal class TriggerReflect : TriggerAction
{
    private string _reflectorName = string.Empty;

    public string ReflectorName
    {
        get => _reflectorName;
        set => SetProperty(ref _reflectorName, value);
    }

    public override Task ExecuteAsync()
    {
        return Globals.Instance.ServiceProvider.GetRequiredService<IReceiver>()
            .Reflect(Context.Character, Context.User, ReflectorName);
    }
}

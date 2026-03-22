using Akagi.Bridge.Attributes;
using Akagi.Data;

namespace Akagi.Characters.Groups.Strategies;

[GraphNode]
internal abstract class GroupStrategy : Savable
{
    private string _name = string.Empty;
    private string _description = string.Empty;

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

    public abstract Task ProcessAsync(GroupContext context);
}

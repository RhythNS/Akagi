namespace Akagi.Utils.Attributes;

[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public class DependsOnAttribute : Attribute
{
    public Type[] DependentTypes { get; private init; }

    public DependsOnAttribute(params Type[] dependentTypes)
    {
        DependentTypes = dependentTypes;
    }
}

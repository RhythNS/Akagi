using Akagi.Utils.Attributes;

namespace Akagi.Utils.Extensions;

public static class DependsOnExtensions
{
    public static IEnumerable<Type> SortByDependencies(this IEnumerable<Type> types)
    {
        List<Type> sorted = [];
        HashSet<Type> visited = [];
        List<Type> typeList = [.. types];

        foreach (Type type in types)
        {
            Visit(type, typeList, visited, sorted);
        }

        return sorted;
    }

    private static DependsOnAttribute? GetDependsOnAttribute(Type type)
    {
        return (DependsOnAttribute?)type.GetCustomAttributes(typeof(DependsOnAttribute), false).FirstOrDefault();
    }

    private static void Visit(Type type, List<Type> types, HashSet<Type> visited, List<Type> sorted)
    {
        if (visited.Contains(type))
        {
            return;
        }
        visited.Add(type);
        DependsOnAttribute? attribute = GetDependsOnAttribute(type);
        if (attribute != null)
        {
            foreach (Type dependency in attribute.DependentTypes)
            {
                if (types.Contains(dependency))
                {
                    Visit(dependency, types, visited, sorted);
                }
            }
        }
        sorted.Add(type);
    }
}

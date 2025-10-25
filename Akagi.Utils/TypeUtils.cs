namespace Akagi.Utils;

public class TypeUtils
{
    public static Type[] GetTypesExtendingFrom<T>() =>
        [.. AppDomain.CurrentDomain.GetAssemblies()
            .SelectMany(assembly => assembly.GetTypes())
            .Where(type => type.IsSubclassOf(typeof(T)))];

    public static Type[] GetNonAbstractTypesExtendingFrom<T>() =>
        [.. AppDomain.CurrentDomain.GetAssemblies()
            .SelectMany(assembly => assembly.GetTypes())
            .Where(type => typeof(T).IsAssignableFrom(type) && !type.IsAbstract)];
}

namespace Akagi.Utils;

internal class TypeUtils
{
    public static Type[] GetNonAbstractTypesExtendingFrom<T>() =>
        [.. AppDomain.CurrentDomain.GetAssemblies()
            .SelectMany(assembly => assembly.GetTypes())
            .Where(type => typeof(T).IsAssignableFrom(type) && !type.IsAbstract)];
}

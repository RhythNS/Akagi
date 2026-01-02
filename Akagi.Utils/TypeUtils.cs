using System.Reflection;

namespace Akagi.Utils;

public class TypeUtils
{
    private static bool _assembliesLoaded = false;
    private static readonly object _loadLock = new();

    private static void EnsureAssembliesLoaded()
    {
        if (_assembliesLoaded)
        {
            return;
        }

        lock (_loadLock)
        {
            if (_assembliesLoaded)
            {
                return;
            }

            string baseDirectory = AppDomain.CurrentDomain.BaseDirectory;
            string[] dllFiles = Directory.GetFiles(baseDirectory, "Akagi*.dll");

            foreach (string dllFile in dllFiles)
            {
                try
                {
                    AssemblyName assemblyName = AssemblyName.GetAssemblyName(dllFile);
                    if (!AppDomain.CurrentDomain.GetAssemblies().Any(a => a.FullName == assemblyName.FullName))
                    {
                        Assembly.Load(assemblyName);
                    }
                }
                catch
                {
                    // Skip assemblies that can't be loaded
                }
            }

            _assembliesLoaded = true;
        }
    }

    public static Type[] GetTypesExtendingFrom<T>()
    {
        EnsureAssembliesLoaded();
        return [.. AppDomain.CurrentDomain.GetAssemblies()
            .SelectMany(assembly => assembly.GetTypes())
            .Where(type => type.IsSubclassOf(typeof(T)))];
    }

    public static Type[] GetNonAbstractTypesExtendingFrom<T>()
    {
        EnsureAssembliesLoaded();
        return [.. AppDomain.CurrentDomain.GetAssemblies()
            .SelectMany(assembly => assembly.GetTypes())
            .Where(type => typeof(T).IsAssignableFrom(type) && !type.IsAbstract)];
    }

    public static Type[] GetTypeWithAttribute<TAttribute>() where TAttribute : Attribute
    {
        EnsureAssembliesLoaded();
        return [.. AppDomain.CurrentDomain.GetAssemblies()
            .SelectMany(assembly => assembly.GetTypes())
            .Where(type => type.GetCustomAttribute<TAttribute>() != null)];
    }

    public static Type[] GetNonAbstractTypeWithAttribute<TAttribute>() where TAttribute : Attribute
    {
        EnsureAssembliesLoaded();
        return [.. AppDomain.CurrentDomain.GetAssemblies()
            .SelectMany(assembly => assembly.GetTypes())
            .Where(type => type.GetCustomAttribute<TAttribute>() != null && !type.IsAbstract)];
    }
}

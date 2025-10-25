using System.Reflection;

namespace Akagi.Utils;

public class ObjectUtils
{
    public static T? ShallowClone<T>(T obj) where T : class, new()
    {
        if (obj == null)
        {
            return default;
        }
        T clone = new();
        foreach (PropertyInfo property in typeof(T).GetProperties())
        {
            if (property.CanWrite)
            {
                property.SetValue(clone, property.GetValue(obj));
            }
        }
        return clone;
    }

    public static T? DeepClone<T>(T obj) where T : class, new()
    {
        if (obj == null)
        {
            return default;
        }

        return (T?)DeepCloneObject(obj);
    }

    // TODO: untested
    private static object? DeepCloneObject(object obj)
    {
        if (obj == null)
        {
            return null;
        }

        Type type = obj.GetType();

        if (type.IsValueType || type == typeof(string))
        {
            return obj;
        }

        if (type.IsArray)
        {
            Type? elementType = type.GetElementType();
            if (elementType == null)
            {
                throw new InvalidOperationException("Array element type cannot be determined.");
            }

            Array? array = obj as Array;
            if (array == null)
            {
                throw new InvalidOperationException("Object is not a valid array.");
            }

            Array copied = Array.CreateInstance(elementType, array.Length);
            for (int i = 0; i < array.Length; i++)
            {
                object? value = array.GetValue(i);
                copied.SetValue(value != null ? DeepCloneObject(value) : null, i);
            }
            return Convert.ChangeType(copied, obj.GetType());
        }

        if (type.IsClass)
        {
            object? clone = Activator.CreateInstance(obj.GetType());
            if (clone == null)
            {
                throw new InvalidOperationException($"Failed to create an instance of type {obj.GetType()}.");
            }

            foreach (PropertyInfo property in type.GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
            {
                if (property.CanWrite)
                {
                    object? propertyValue = property.GetValue(obj);
                    property.SetValue(clone, propertyValue != null ? DeepCloneObject(propertyValue) : null);
                }
            }

            foreach (FieldInfo field in type.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
            {
                object? fieldValue = field.GetValue(obj);
                field.SetValue(clone, fieldValue != null ? DeepCloneObject(fieldValue) : null);
            }

            return clone;
        }

        throw new ArgumentException("Unknown type");
    }
}

using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Reflection;

namespace DeltaZulu.Agent.Inputs.Windows;

internal static class ObjectDictionaryExtensions
{
    public static IDictionary<string, object?> AsDictionary(this object source, BindingFlags bindingAttr = BindingFlags.Public | BindingFlags.Instance)
    {
        var result = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        foreach (var propertyInfo in source.GetType().GetProperties(bindingAttr))
        {
            object? value;
            try
            {
                value = propertyInfo.GetValue(source, null);
            }
            catch
            {
                continue;
            }

            if (value is null)
            {
                continue;
            }

            if (IsSimple(value.GetType()))
            {
                result[propertyInfo.Name] = value;
            }
            else
            {
                try
                {
                    var json = JsonConvert.SerializeObject(value, Formatting.None, new JsonSerializerSettings
                    {
                        NullValueHandling = NullValueHandling.Ignore,
                        ReferenceLoopHandling = ReferenceLoopHandling.Ignore
                    });
                    result[propertyInfo.Name] = JToken.Parse(json).ToObject<object>();
                }
                catch
                {
                    result[propertyInfo.Name] = value.ToString();
                }
            }
        }

        return result;
    }

    private static bool IsSimple(Type type)
    {
        type = Nullable.GetUnderlyingType(type) ?? type;
        return type.IsPrimitive
               || type.IsEnum
               || type == typeof(string)
               || type == typeof(DateTime)
               || type == typeof(DateTimeOffset)
               || type == typeof(Guid)
               || type == typeof(decimal);
    }
}
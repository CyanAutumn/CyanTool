using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace CyanTools.Pipeline
{
    public static class PipelineSnapshotHelper
    {
        private static readonly string[] SensitiveNameFragments =
        {
            "password",
            "passwd",
            "secret",
            "token",
            "api_key",
            "apikey",
            "cookie",
            "credential",
            "authorization",
            "bearer"
        };

        public static Dictionary<string, object?> CaptureSimpleProperties(object target)
        {
            var dict = new Dictionary<string, object?>();
            var props = target.GetType()
                .GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(p => p.CanRead && p.CanWrite && IsSimpleType(p.PropertyType))
                .Where(p => IsSafePropertyName(p.Name));

            foreach (var prop in props)
            {
                dict[prop.Name] = prop.GetValue(target);
            }

            return dict;
        }

        public static void ApplySimpleProperties(object target, Dictionary<string, object?> snapshot)
        {
            var props = target.GetType()
                .GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(p => p.CanWrite && p.CanRead && IsSimpleType(p.PropertyType))
                .Where(p => IsSafePropertyName(p.Name))
                .ToDictionary(p => p.Name, p => p, StringComparer.Ordinal);

            foreach (var kvp in snapshot)
            {
                if (!props.TryGetValue(kvp.Key, out var prop))
                {
                    continue;
                }

                try
                {
                    var value = ConvertToType(kvp.Value, prop.PropertyType);
                    prop.SetValue(target, value);
                }
                catch
                {

                }
            }
        }

        private static object? ConvertToType(object? value, Type targetType)
        {
            if (value == null)
            {
                return null;
            }

            var underlying = Nullable.GetUnderlyingType(targetType) ?? targetType;

            if (underlying.IsEnum)
            {
                return Enum.Parse(underlying, value.ToString()!);
            }

            if (underlying == typeof(Guid))
            {
                return Guid.Parse(value.ToString()!);
            }

            return Convert.ChangeType(value, underlying);
        }

        private static bool IsSimpleType(Type type)
        {
            var underlying = Nullable.GetUnderlyingType(type) ?? type;
            return underlying.IsPrimitive
                   || underlying.IsEnum
                   || underlying == typeof(string)
                   || underlying == typeof(decimal)
                   || underlying == typeof(DateTime)
                   || underlying == typeof(double)
                   || underlying == typeof(float)
                   || underlying == typeof(int)
                   || underlying == typeof(long)
                   || underlying == typeof(bool);
        }

        private static bool IsSafePropertyName(string propertyName)
        {
            string normalized = propertyName.Replace("_", string.Empty, StringComparison.Ordinal)
                .Replace("-", string.Empty, StringComparison.Ordinal)
                .ToLowerInvariant();
            return !SensitiveNameFragments.Any(fragment =>
                normalized.Contains(fragment.Replace("_", string.Empty, StringComparison.Ordinal), StringComparison.Ordinal));
        }
    }
}

using System.IO;
using System.Reflection;
using System.Text.Json;

namespace CyanTools.Utils;





public sealed class InputStateStore
{
    private static readonly string DefaultFilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "CyanTools",
        "user-control-input-state.json");

    private static readonly string[] SensitiveNameFragments =
    [
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
    ];

    private static readonly string[] RuntimeNameFragments =
    [
        "log",
        "status",
        "progress",
        "error",
        "exception",
        "isbusy",
        "isidle",
        "isrunning",
        "isdownloading",
        "isprocessing"
    ];

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    private readonly object _syncRoot = new();
    private readonly string _stateFilePath;
    private Dictionary<string, Dictionary<string, JsonElement>> _states = new(StringComparer.Ordinal);
    private bool _isLoaded;

    public InputStateStore(string? stateFilePath = null)
    {
        _stateFilePath = string.IsNullOrWhiteSpace(stateFilePath) ? DefaultFilePath : stateFilePath;
    }

    public static InputStateStore Default { get; } = new();

    public void Save(object target)
    {
        ArgumentNullException.ThrowIfNull(target);

        try
        {
            lock (_syncRoot)
            {
                EnsureLoaded();

                var state = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
                foreach (PropertyInfo property in GetPersistableProperties(target.GetType()))
                {
                    try
                    {
                        state[property.Name] = JsonSerializer.SerializeToElement(MaterialClassificationSession.GetPersistedValue(target, property), property.PropertyType, SerializerOptions);
                    }
                    catch
                    {


                    }
                }

                _states[GetStateKey(target.GetType())] = state;
                WriteStates();
            }
        }
        catch
        {


        }
    }

    public void Restore(object target)
    {
        ArgumentNullException.ThrowIfNull(target);

        try
        {
            lock (_syncRoot)
            {
                EnsureLoaded();
                if (!_states.TryGetValue(GetStateKey(target.GetType()), out Dictionary<string, JsonElement>? state))
                {
                    return;
                }

                foreach (PropertyInfo property in GetPersistableProperties(target.GetType()))
                {
                    if (!state.TryGetValue(property.Name, out JsonElement value))
                    {
                        continue;
                    }

                    try
                    {
                        object? restoredValue = value.Deserialize(property.PropertyType, SerializerOptions);
                        property.SetValue(target, restoredValue);
                    }
                    catch
                    {

                    }
                }
            }
        }
        catch
        {


        }
    }

    private void EnsureLoaded()
    {
        if (_isLoaded)
        {
            return;
        }

        _isLoaded = true;
        try
        {
            if (!File.Exists(_stateFilePath))
            {
                return;
            }

            string json = File.ReadAllText(_stateFilePath);
            _states = JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, JsonElement>>>(json, SerializerOptions)
                ?? new Dictionary<string, Dictionary<string, JsonElement>>(StringComparer.Ordinal);
        }
        catch
        {
            _states = new Dictionary<string, Dictionary<string, JsonElement>>(StringComparer.Ordinal);
        }
    }

    private void WriteStates()
    {
        string? directory = Path.GetDirectoryName(_stateFilePath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        string temporaryPath = _stateFilePath + ".tmp";
        try
        {
            File.WriteAllText(temporaryPath, JsonSerializer.Serialize(_states, SerializerOptions));
            File.Move(temporaryPath, _stateFilePath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    private static IEnumerable<PropertyInfo> GetPersistableProperties(Type targetType) =>
        targetType
            .GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Where(property => property.GetMethod?.IsPublic == true && property.SetMethod?.IsPublic == true)
            .Where(property => IsSafeInputName(property.Name))
            .Where(property => IsSupportedScalarType(property.PropertyType));

    private static bool IsSafeInputName(string propertyName)
    {
        string normalized = propertyName.Replace("-", string.Empty, StringComparison.Ordinal)
            .Replace("_", string.Empty, StringComparison.Ordinal)
            .ToLowerInvariant();

        return !SensitiveNameFragments.Any(fragment => normalized.Contains(fragment.Replace("_", string.Empty, StringComparison.Ordinal), StringComparison.Ordinal))
            && !RuntimeNameFragments.Any(fragment => normalized.Contains(fragment, StringComparison.Ordinal));
    }

    private static bool IsSupportedScalarType(Type propertyType)
    {
        Type type = Nullable.GetUnderlyingType(propertyType) ?? propertyType;
        return type.IsEnum ||
            type == typeof(string) ||
            type == typeof(bool) ||
            type == typeof(char) ||
            type == typeof(byte) ||
            type == typeof(sbyte) ||
            type == typeof(short) ||
            type == typeof(ushort) ||
            type == typeof(int) ||
            type == typeof(uint) ||
            type == typeof(long) ||
            type == typeof(ulong) ||
            type == typeof(float) ||
            type == typeof(double) ||
            type == typeof(decimal) ||
            type == typeof(DateTime) ||
            type == typeof(DateTimeOffset) ||
            type == typeof(TimeSpan) ||
            type == typeof(Guid);
    }

    private static string GetStateKey(Type targetType) => targetType.FullName ?? targetType.Name;
}

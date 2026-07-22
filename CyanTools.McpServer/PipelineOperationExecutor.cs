using System.Collections;
using System.Globalization;
using System.Reflection;
using System.Text.Json;
using CyanTools.Agent;
using CyanTools.Pipeline;
using CyanTools.ViewModel;

namespace CyanTools.McpServer;

public sealed record PipelineOperationResult(
    string OperationId,
    string Status,
    string InputDirectory,
    string? OutputDirectory,
    IReadOnlyList<string> Logs);





public sealed class PipelineOperationExecutor
{
    private const int MaximumReturnedLogLines = 200;

    public static object DescribeParameters(AgentOperationDefinition operation)
    {
        PipelineNodeViewModelBase node = CreateNode(operation);
        var parameters = PipelineNodeParameterFactory.Create(node)
            .Select(parameter => new
            {
                name = parameter.PropertyName,
                label = parameter.Label,
                type = GetParameterType(parameter),
                allowed_values = parameter.Options.Select(option => new
                {
                    value = Convert.ToString(option.Value, CultureInfo.InvariantCulture),
                    label = option.DisplayName
                }),
                description = "仅提交此字段；未知、只读、进度和凭据字段会被拒绝。"
            });

        return new
        {
            common_parameters = operation.Parameters,
            parameters
        };
    }

    public async Task<PipelineOperationResult> ExecuteAsync(
        string operationId,
        string inputDirectory,
        string? outputDirectory,
        string? parametersJson,
        bool confirmDestructive,
        CancellationToken cancellationToken)
    {
        AgentOperationDefinition operation = AgentToolCatalog.GetOperation(operationId);
        if (!operation.IsHeadlessExecutable)
        {
            throw new InvalidOperationException($"{operationId} still requires a dedicated headless adapter and cannot be executed through MCP.");
        }

        if (operation.Risk == AgentOperationRisk.Destructive && !confirmDestructive)
        {
            throw new InvalidOperationException($"{operationId} can delete, rename, or overwrite data. Call cyantools_describe_operation, obtain explicit user confirmation, then set confirm_destructive to true.");
        }

        string normalizedInput = AllowedPathGuard.RequireDirectory(inputDirectory, "input_dir");
        string? normalizedOutput = string.IsNullOrWhiteSpace(outputDirectory)
            ? null
            : AllowedPathGuard.RequirePathWithinAllowedRoot(outputDirectory, "output_dir");

        PipelineNodeViewModelBase node = CreateNode(operation);
        ApplyParameters(node, parametersJson);

        IPipelineDirectoryNode? directoryNode = node.GetDirectoryAutomationNode();
        if (directoryNode == null)
        {
            throw new InvalidOperationException($"{operationId} is not a directory-based pipeline operation and needs a dedicated MCP adapter.");
        }

        directoryNode.IsWorkflowMode = true;
        directoryNode.HasInheritedInputPath = false;
        directoryNode.PipelineInputPath = normalizedInput;
        directoryNode.PipelineOutputPath = normalizedOutput ?? string.Empty;

        var logs = new List<string>();
        void AppendLog(string message)
        {
            if (logs.Count < MaximumReturnedLogLines)
            {
                logs.Add(message);
            }
        }

        var context = new PipelineContext(AppendLog);
        context.UpdateEffectivePaths(normalizedInput, normalizedOutput ?? string.Empty, 1);
        node.AttachLogger(AppendLog);

        await node.RunAsync(context, cancellationToken).ConfigureAwait(false);

        string? resolvedOutput = string.IsNullOrWhiteSpace(directoryNode.PipelineOutputPath)
            ? null
            : directoryNode.PipelineOutputPath;
        return new PipelineOperationResult(operation.Id, "completed", normalizedInput, resolvedOutput, logs);
    }

    private static PipelineNodeViewModelBase CreateNode(AgentOperationDefinition operation)
    {
        if (operation.ExecutionKind != AgentExecutionKind.PipelineNode || string.IsNullOrWhiteSpace(operation.PipelineNodeType))
        {
            throw new InvalidOperationException($"{operation.Id} is not backed by a pipeline node.");
        }

        PipelineNodeDescriptor? descriptor = PipelineCatalog.GetAll()
            .FirstOrDefault(candidate => string.Equals(candidate.NodeType.FullName, operation.PipelineNodeType, StringComparison.Ordinal));
        return descriptor?.Factory()
            ?? throw new InvalidOperationException($"The registered pipeline node for {operation.Id} is unavailable.");
    }

    private static void ApplyParameters(PipelineNodeViewModelBase node, string? parametersJson)
    {
        if (string.IsNullOrWhiteSpace(parametersJson))
        {
            return;
        }

        using JsonDocument document = JsonDocument.Parse(parametersJson);
        if (document.RootElement.ValueKind != JsonValueKind.Object)
        {
            throw new ArgumentException("parameters_json must be a JSON object.", nameof(parametersJson));
        }

        IReadOnlyDictionary<string, PipelineNodeParameterViewModel> permitted = PipelineNodeParameterFactory.Create(node)
            .ToDictionary(parameter => parameter.PropertyName, StringComparer.Ordinal);

        object target = ResolveParameterTarget(node);
        foreach (JsonProperty jsonProperty in document.RootElement.EnumerateObject())
        {
            if (!permitted.TryGetValue(jsonProperty.Name, out PipelineNodeParameterViewModel? parameter))
            {
                throw new ArgumentException($"{jsonProperty.Name} is not an allowed parameter for this operation.", nameof(parametersJson));
            }

            PropertyInfo property = target.GetType().GetProperty(parameter.PropertyName, BindingFlags.Public | BindingFlags.Instance)
                ?? throw new InvalidOperationException($"Cannot resolve {parameter.PropertyName} on the pipeline node.");
            object? value = ConvertJsonValue(jsonProperty.Value, property.PropertyType, parameter.Options);
            property.SetValue(target, value);
        }
    }

    private static object ResolveParameterTarget(PipelineNodeViewModelBase node)
    {
        PropertyInfo? inner = node.GetType().GetProperty("Inner", BindingFlags.Public | BindingFlags.Instance);
        return inner?.GetValue(node) ?? node;
    }

    private static object? ConvertJsonValue(
        JsonElement value,
        Type propertyType,
        IReadOnlyList<PipelineNodeParameterOption> options)
    {
        Type targetType = Nullable.GetUnderlyingType(propertyType) ?? propertyType;
        if (value.ValueKind == JsonValueKind.Null)
        {
            if (Nullable.GetUnderlyingType(propertyType) != null || !targetType.IsValueType)
            {
                return null;
            }

            throw new ArgumentException($"{targetType.Name} cannot be null.");
        }

        if (options.Count > 0)
        {
            string requested = value.GetString() ?? value.ToString();
            PipelineNodeParameterOption? option = options.FirstOrDefault(candidate =>
                string.Equals(Convert.ToString(candidate.Value, CultureInfo.InvariantCulture), requested, StringComparison.OrdinalIgnoreCase)
                || string.Equals(candidate.DisplayName, requested, StringComparison.OrdinalIgnoreCase));
            if (option != null)
            {
                return option.Value;
            }
        }

        if (targetType == typeof(string))
        {
            return value.GetString() ?? value.ToString();
        }

        if (targetType == typeof(bool))
        {
            return value.GetBoolean();
        }

        if (targetType == typeof(int))
        {
            return value.GetInt32();
        }

        if (targetType == typeof(double))
        {
            return value.GetDouble();
        }

        if (targetType == typeof(float))
        {
            return value.GetSingle();
        }

        if (targetType.IsEnum)
        {
            return Enum.Parse(targetType, value.GetString() ?? value.ToString(), ignoreCase: true);
        }

        throw new ArgumentException($"{targetType.Name} is not a supported MCP parameter type.");
    }

    private static string GetParameterType(PipelineNodeParameterViewModel parameter)
    {
        if (parameter.Options.Count > 0)
        {
            return "choice";
        }

        return parameter.EditorKind switch
        {
            PipelineParameterEditorKind.Boolean => "boolean",
            _ => "string_or_number"
        };
    }
}

internal static class AllowedPathGuard
{
    private const string AllowedRootsVariable = "CYANTOOLS_MCP_ALLOWED_ROOTS";

    public static string RequireDirectory(string path, string argumentName)
    {
        string normalizedPath = RequirePathWithinAllowedRoot(path, argumentName);
        if (!Directory.Exists(normalizedPath))
        {
            throw new DirectoryNotFoundException($"{argumentName} does not exist: {normalizedPath}");
        }

        return normalizedPath;
    }

    public static string RequirePathWithinAllowedRoot(string path, string argumentName)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException($"{argumentName} is required.", argumentName);
        }

        IReadOnlyList<string> allowedRoots = GetAllowedRoots();
        if (allowedRoots.Count == 0)
        {
            throw new InvalidOperationException($"Set {AllowedRootsVariable} to one or more approved local roots before allowing MCP file operations.");
        }

        string normalizedPath = Path.GetFullPath(path.Trim());
        if (!allowedRoots.Any(root => IsWithinRoot(normalizedPath, root)))
        {
            throw new UnauthorizedAccessException($"{argumentName} is outside the configured MCP allowed roots.");
        }

        return normalizedPath;
    }

    private static IReadOnlyList<string> GetAllowedRoots() =>
        (Environment.GetEnvironmentVariable(AllowedRootsVariable) ?? string.Empty)
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(Path.GetFullPath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private static bool IsWithinRoot(string path, string root)
    {
        string normalizedRoot = root.EndsWith(Path.DirectorySeparatorChar)
            ? root
            : root + Path.DirectorySeparatorChar;
        return string.Equals(path, root, StringComparison.OrdinalIgnoreCase)
            || path.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase);
    }
}

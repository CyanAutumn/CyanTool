using CyanTools.Agent;
using CyanTools.Pipeline;
using CyanTools.ViewModel;

namespace CyanTools.McpServer;

public sealed record WorkflowNodeDescription(
    int Sequence,
    string DisplayName,
    string NodeType,
    string? OutputPortKey);

public sealed record WorkflowFirstNodeDescription(
    string DisplayName,
    string NodeType,
    IReadOnlyList<PipelineParameterDescription> Parameters);

public sealed record WorkflowSummary(
    string Name,
    string Status,
    int NodeCount,
    string? FirstNodeDisplayName,
    string? Risk,
    bool RequiresDestructiveConfirmation,
    string? Error = null);

public sealed record WorkflowDescription(
    string Name,
    int NodeCount,
    string Risk,
    bool RequiresDestructiveConfirmation,
    IReadOnlyList<WorkflowNodeDescription> Nodes,
    WorkflowFirstNodeDescription FirstNode,
    string OverrideScope);

public sealed record WorkflowExecutionResult(
    string WorkflowName,
    string Status,
    string Risk,
    int NodeCount,
    string FirstNodeDisplayName,
    IReadOnlyDictionary<string, object?> FirstNodeParameters,
    IReadOnlyList<string> Logs);





public sealed class WorkflowOperationExecutor
{
    private const int MaximumReturnedLogLines = 200;
    private readonly string _workflowFolder;
    private readonly PipelineRunner _runner = new();

    public WorkflowOperationExecutor(string? workflowFolder = null)
    {
        _workflowFolder = WorkflowStorage.EnsureFolder(workflowFolder);
    }

    public IReadOnlyList<WorkflowSummary> ListWorkflows()
    {
        return WorkflowStorage.ListWorkflows(_workflowFolder)
            .Select(CreateSummary)
            .ToArray();
    }

    public WorkflowDescription DescribeWorkflow(string workflowName)
    {
        IReadOnlyList<PipelineNodeViewModelBase> nodes = LoadRequiredWorkflow(workflowName);
        AgentOperationRisk risk = ResolveWorkflowRisk(nodes);
        PipelineNodeViewModelBase firstNode = nodes[0];

        return new WorkflowDescription(
            workflowName.Trim(),
            nodes.Count,
            FormatRisk(risk),
            risk == AgentOperationRisk.Destructive,
            nodes.Select((node, index) => new WorkflowNodeDescription(
                    index + 1,
                    node.DisplayName,
                    node.GetType().FullName ?? node.GetType().Name,
                    node.WorkflowOutputPortKey))
                .ToArray(),
            new WorkflowFirstNodeDescription(
                firstNode.DisplayName,
                firstNode.GetType().FullName ?? firstNode.GetType().Name,
                PipelineOperationExecutor.DescribeNodeParameters(firstNode)),
            "first_node_only; omitted first-node fields and every later node keep the saved workflow values");
    }

    public async Task<WorkflowExecutionResult> ExecuteAsync(
        string workflowName,
        string? firstNodeParametersJson,
        bool confirmDestructive,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<PipelineNodeViewModelBase> nodes = LoadRequiredWorkflow(workflowName);
        ValidateWorkflow(nodes);

        AgentOperationRisk risk = ResolveWorkflowRisk(nodes);
        if (risk == AgentOperationRisk.EnvironmentChange)
        {
            throw new InvalidOperationException("Environment-changing nodes cannot be executed from a saved workflow through MCP.");
        }

        if (risk == AgentOperationRisk.Destructive && !confirmDestructive)
        {
            throw new InvalidOperationException(
                $"{workflowName} contains a destructive node. Describe the workflow, obtain explicit user confirmation, then set confirm_destructive to true.");
        }

        PipelineNodeViewModelBase firstNode = nodes[0];
        PipelineOperationExecutor.ApplyParameters(firstNode, firstNodeParametersJson);
        risk = ResolveWorkflowRisk(nodes);
        foreach (PipelineNodeViewModelBase node in nodes)
        {
            PipelineOperationExecutor.RequireCleanupConfirmation(node, confirmDestructive);
            node.SetWorkflowEditorMode(true);
        }

        var logs = new List<string>();
        void AppendLog(string message)
        {
            lock (logs)
            {
                if (logs.Count < MaximumReturnedLogLines) logs.Add(message);
            }
        }

        var context = new PipelineContext(AppendLog);
        await _runner.RunAsync(
                nodes,
                context,
                cancellationToken,
                node => AllowedPathGuard.ValidateNodePaths(node, context))
            .ConfigureAwait(false);

        IReadOnlyDictionary<string, object?> appliedFirstNodeParameters =
            PipelineOperationExecutor.DescribeNodeParameters(firstNode)
                .ToDictionary(parameter => parameter.Name, parameter => parameter.DefaultValue, StringComparer.Ordinal);

        return new WorkflowExecutionResult(
            workflowName.Trim(),
            "completed",
            FormatRisk(risk),
            nodes.Count,
            firstNode.DisplayName,
            appliedFirstNodeParameters,
            logs);
    }

    private WorkflowSummary CreateSummary(string workflowName)
    {
        try
        {
            IReadOnlyList<PipelineNodeViewModelBase> nodes = LoadRequiredWorkflow(workflowName);
            AgentOperationRisk risk = ResolveWorkflowRisk(nodes);
            return new WorkflowSummary(
                workflowName,
                "ready",
                nodes.Count,
                nodes[0].DisplayName,
                FormatRisk(risk),
                risk == AgentOperationRisk.Destructive);
        }
        catch (Exception exception)
        {
            return new WorkflowSummary(
                workflowName,
                "invalid",
                0,
                null,
                null,
                false,
                exception.Message);
        }
    }

    private IReadOnlyList<PipelineNodeViewModelBase> LoadRequiredWorkflow(string workflowName)
    {
        string normalizedName = workflowName?.Trim() ?? string.Empty;
        IReadOnlyList<PipelineNodeViewModelBase> nodes =
            WorkflowStorage.Load(normalizedName, downloadersUnlocked: true, _workflowFolder);
        if (nodes.Count == 0)
        {
            throw new KeyNotFoundException($"Workflow is missing or contains no executable nodes: {normalizedName}");
        }

        return nodes;
    }

    private static void ValidateWorkflow(IReadOnlyList<PipelineNodeViewModelBase> nodes)
    {
        string? downloaderError = PipelineWorkflowValidation.ValidateSingletonFirstNode(
            nodes,
            node => node is DatasetDownloaderPipelineNode,
            "数据集管理器");
        if (!string.IsNullOrWhiteSpace(downloaderError))
        {
            throw new InvalidOperationException(downloaderError);
        }

        string? videoError = PipelineWorkflowValidation.ValidateSingletonFirstNode(
            nodes,
            node => node is ExportVideoFramePipelineNode,
            "导出视频帧");
        if (!string.IsNullOrWhiteSpace(videoError))
        {
            throw new InvalidOperationException(videoError);
        }

        for (int index = 0; index < nodes.Count; index++)
        {
            IPipelineOutputPathSelectorNode? selector = nodes[index].GetOutputPathSelectorNode();
            if (selector == null)
            {
                continue;
            }

            bool followsGeneratedRootOutput =
                index > 0 &&
                nodes[index - 1].GetDirectoryAutomationNode()?.DirectoryBehavior == PipelineDirectoryBehavior.GeneratedOutput &&
                string.Equals(nodes[index - 1].WorkflowOutputPortKey, "root", StringComparison.OrdinalIgnoreCase);
            if (!followsGeneratedRootOutput || !selector.TryGetOutputFolderPath(out _))
            {
                throw new InvalidOperationException("选择输出文件夹必须连接到上游节点的总输出端口，并设置有效目标目录。");
            }
        }
    }

    private static AgentOperationRisk ResolveWorkflowRisk(IReadOnlyList<PipelineNodeViewModelBase> nodes)
    {
        AgentOperationRisk risk = AgentOperationRisk.ReadOnly;
        foreach (PipelineNodeViewModelBase node in nodes)
        {
            AgentOperationRisk nodeRisk = ResolveNodeRisk(node);
            if (GetRiskRank(nodeRisk) > GetRiskRank(risk))
            {
                risk = nodeRisk;
            }
        }

        return risk;
    }

    private static AgentOperationRisk ResolveNodeRisk(PipelineNodeViewModelBase node)
    {
        if (PipelineOperationExecutor.ClearsInput(node)) return AgentOperationRisk.Destructive;
        string nodeType = node.GetType().FullName ?? node.GetType().Name;
        AgentOperationDefinition? operation = AgentToolCatalog.GetPages()
            .SelectMany(page => page.Operations)
            .FirstOrDefault(candidate =>
                candidate.ExecutionKind == AgentExecutionKind.PipelineNode &&
                string.Equals(candidate.PipelineNodeType, nodeType, StringComparison.Ordinal));
        if (operation != null)
        {
            return operation.Risk;
        }

        if (node is FolderPickerPipelineNode || node.GetOutputPathSelectorNode() != null)
        {
            return AgentOperationRisk.ReadOnly;
        }

        return AgentOperationRisk.WritesOutput;
    }

    private static int GetRiskRank(AgentOperationRisk risk) => risk switch
    {
        AgentOperationRisk.EnvironmentChange => 5,
        AgentOperationRisk.Destructive => 4,
        AgentOperationRisk.ExternalService => 3,
        AgentOperationRisk.WritesOutput => 2,
        _ => 1
    };

    private static string FormatRisk(AgentOperationRisk risk) => risk switch
    {
        AgentOperationRisk.ReadOnly => "read_only",
        AgentOperationRisk.WritesOutput => "writes_output",
        AgentOperationRisk.Destructive => "destructive",
        AgentOperationRisk.ExternalService => "external_service",
        AgentOperationRisk.EnvironmentChange => "environment_change",
        _ => risk.ToString().ToLowerInvariant()
    };
}

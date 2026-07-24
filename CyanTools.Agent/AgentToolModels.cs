namespace CyanTools.Agent;





public enum AgentOperationRisk
{
    ReadOnly,
    WritesOutput,
    Destructive,
    ExternalService,
    EnvironmentChange
}




public enum AgentExecutionKind
{
    PipelineNode,
    Workflow,
    DedicatedAdapter
}

public sealed record AgentParameterDefinition(
    string Name,
    string Description,
    bool Required = false,
    string? Type = null);

public sealed record AgentOperationDefinition(
    string Id,
    string DisplayName,
    string Description,
    AgentOperationRisk Risk,
    AgentExecutionKind ExecutionKind,
    string? PipelineNodeType = null,
    IReadOnlyList<AgentParameterDefinition>? Parameters = null)
{
    public bool IsHeadlessExecutable =>
        ExecutionKind is AgentExecutionKind.PipelineNode or AgentExecutionKind.Workflow;
}

public sealed record AgentPageDefinition(
    string Id,
    string DisplayName,
    string Category,
    string Description,
    IReadOnlyList<AgentOperationDefinition> Operations);

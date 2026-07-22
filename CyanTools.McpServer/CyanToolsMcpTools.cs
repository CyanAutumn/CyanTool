using System.ComponentModel;
using System.Text.Json;
using CyanTools.Agent;
using CyanTools.Pipeline;
using CyanTools.ViewModel;
using ModelContextProtocol.Server;

namespace CyanTools.McpServer;






[McpServerToolType]
public sealed class CyanToolsMcpTools
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };

    private readonly PipelineOperationExecutor _pipelineExecutor;

    public CyanToolsMcpTools(PipelineOperationExecutor pipelineExecutor)
    {
        _pipelineExecutor = pipelineExecutor;
    }

    [McpServerTool(Name = "cyantools_list_pages", Title = "列出 CyanTools 功能页", ReadOnly = true, Destructive = false), Description("列出 CyanTools 的全部功能页及其 Agent 操作。先用此工具查找能力，再用 cyantools_describe_operation 查看精确参数和风险。")]
    public static string CyantoolsListPages(
        [Description("可选分类：训练、数据集获取、数据集归类、数据集处理、环境设置。为空时返回全部。")]
        string? category = null)
    {
        IEnumerable<AgentPageDefinition> pages = AgentToolCatalog.GetPages();
        if (!string.IsNullOrWhiteSpace(category))
        {
            pages = pages.Where(page => string.Equals(page.Category, category.Trim(), StringComparison.OrdinalIgnoreCase));
        }

        return JsonSerializer.Serialize(pages, JsonOptions);
    }

    [McpServerTool(Name = "cyantools_describe_operation", Title = "描述 CyanTools 操作", ReadOnly = true, Destructive = false), Description("返回一个 CyanTools Agent 操作的用途、风险、执行状态，以及页面当前允许的精确参数。必须在调用 cyantools_run_pipeline_operation 前先调用本工具。")]
    public static string CyantoolsDescribeOperation(
        [Description("来自 cyantools_list_pages 的 operation id，例如 images_convert_format。")]
        string operationId)
    {
        AgentOperationDefinition operation = AgentToolCatalog.GetOperation(operationId);
        object response = operation.IsHeadlessExecutable
            ? new
            {
                operation,
                runtime_parameters = PipelineOperationExecutor.DescribeParameters(operation)
            }
            : new
            {
                operation,
                migration_status = "dedicated_adapter_required",
                guidance = "该功能页已经具有稳定的 Agent 合约，但仍需提取无界面服务后才可执行。不要尝试模拟 WPF 按钮或文件选择对话框。"
            };

        return JsonSerializer.Serialize(response, JsonOptions);
    }

    [McpServerTool(Name = "cyantools_run_pipeline_operation", Title = "执行 CyanTools 管道操作", Destructive = true, ReadOnly = false), Description("执行已迁移为无界面管道节点的 CyanTools 操作。执行前必须先调用 cyantools_describe_operation；目录必须在 CYANTOOLS_MCP_ALLOWED_ROOTS 配置的白名单内。对 destructive 操作必须将 confirm_destructive 设为 true。")]
    public async Task<string> CyantoolsRunPipelineOperation(
        [Description("来自 cyantools_describe_operation 的可执行 operation id。")]
        string operationId,
        [Description("存在的输入目录，且必须位于允许根目录内。")]
        string inputDir,
        [Description("可选输出目录；留空时使用该页面原有的默认规则。")]
        string? outputDir = null,
        [Description("页面专属参数 JSON。字段必须来自 cyantools_describe_operation 的 runtime_parameters。")]
        string? parametersJson = null,
        [Description("仅当操作风险为 destructive 时设为 true；这表示用户已明确确认删除、重命名或覆盖后果。")]
        bool confirmDestructive = false,
        CancellationToken cancellationToken = default)
    {
        PipelineOperationResult result = await _pipelineExecutor.ExecuteAsync(
            operationId,
            inputDir,
            outputDir,
            parametersJson,
            confirmDestructive,
            cancellationToken).ConfigureAwait(false);

        return JsonSerializer.Serialize(result, JsonOptions);
    }

    [McpServerTool(Name = "cyantools_get_safety_guide", Title = "读取 CyanTools MCP 安全规则", ReadOnly = true, Destructive = false), Description("说明 CyanTools MCP 的目录白名单、破坏性操作确认和未迁移页面规则。")]
    public static string CyantoolsGetSafetyGuide() => JsonSerializer.Serialize(new
    {
        allowed_roots = "通过 CYANTOOLS_MCP_ALLOWED_ROOTS 设置，以分号分隔。未设置时，所有文件操作都会被拒绝。",
        destructive_operations = "先调用 describe，再由用户明确确认；调用时必须将 confirm_destructive 设为 true。",
        credentials = "凭据继续由 CyanTools 本地配置保管，MCP 不会读取或返回密钥。",
        migration = "DedicatedAdapter 操作不可执行，直到其 UI 行为被重构为无界面服务。"
    }, JsonOptions);

    internal static string SerializeCatalog() => JsonSerializer.Serialize(AgentToolCatalog.GetPages(), JsonOptions);
}

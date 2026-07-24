using System.Collections.ObjectModel;

namespace CyanTools.Agent;






public static class AgentToolCatalog
{
    private static readonly IReadOnlyList<AgentPageDefinition> Pages =
        new ReadOnlyCollection<AgentPageDefinition>(
        [
            Page("workflow", "工作流", "训练", "创建、检查并执行由多个工具步骤组成的本地工作流。",
                Dedicated("workflow_manage", "管理工作流", "读取、保存或调整工作流定义。", AgentOperationRisk.WritesOutput)),
            Page("llm", "LLM工具", "训练", "用兼容的视觉语言模型批量分析图片并生成文本结果。",
                Dedicated("llm_analyze_images", "分析图片", "将图片和提示词提交给已配置的模型服务。", AgentOperationRisk.ExternalService)),
            Page("image_super_resolution", "图片超分", "数据集处理", "使用 Real-ESRGAN 或 Waifu2x 放大目录中的图像。",
                Pipeline("images_super_resolve", "执行图片超分", "对输入目录执行已配置的超分流程。", "CyanTools.ViewModel.RealEsrganPipelineNode", AgentOperationRisk.WritesOutput)),
            Page("image_scaling", "图片上下采", "数据集处理", "按目标尺寸批量上采样或下采样图像。",
                Pipeline("images_scale", "缩放图片", "按指定尺寸处理输入目录中的图片。", "CyanTools.ViewModel.ImageScalingPipelineNode", AgentOperationRisk.WritesOutput)),
            Page("image_processing", "图片处理", "数据集处理", "执行通用图像处理和镜像等交互式处理操作。",
                Dedicated("images_process", "处理图片", "执行页面中选择的图像处理操作。", AgentOperationRisk.WritesOutput)),
            Page("dataset_manager", "数据集管理器", "数据集处理", "浏览、整理、分类并维护数据集图片及其标签。",
                Dedicated("dataset_manage", "管理数据集", "执行数据集分类、标签编辑或文件整理操作。", AgentOperationRisk.Destructive)),
            Page("training_set_manager", "训练集管理器", "训练", "以 Git/LFS 项目管理训练集历史、节点、配置和产物。",
                Dedicated("training_set_manage", "管理训练集项目", "执行训练集项目、提交、分支和配置比较操作。", AgentOperationRisk.Destructive)),
            Page("model_training", "模型训练", "训练", "生成并运行基础模型训练配置。",
                Dedicated("model_training_run", "运行模型训练", "启动已明确配置的模型训练进程。", AgentOperationRisk.ExternalService)),
            Page("layered_lora_merge", "LoRA分层融合", "训练", "按层或 Anima block 权重合并 safetensors LoRA/模型文件。",
                Dedicated("lora_merge_layered", "分层融合 LoRA", "以明确的权重配置生成新的 safetensors 文件。", AgentOperationRisk.WritesOutput)),
            Page("training_bridge", "训练", "训练", "在四种训练后端之间导入、编辑和导出配置。",
                Dedicated("training_bridge_convert_config", "转换训练配置", "导入并导出目标训练后端的配置包。", AgentOperationRisk.WritesOutput)),
            Page("lora_training", "LoRA训练", "训练", "生成并运行 LoRA 训练配置。",
                Dedicated("lora_training_run", "运行 LoRA 训练", "启动已明确配置的 LoRA 训练进程。", AgentOperationRisk.ExternalService)),
            Page("image_bucketing", "图片分辨率归类", "数据集归类", "按尺寸、比例和整除规则归类图片。",
                Pipeline("images_bucket", "归类图片尺寸", "按分辨率规则整理输入目录。", "CyanTools.ViewModel.ImageBucketingPipelineNode", AgentOperationRisk.WritesOutput)),
            Page("three_stage", "图片三分法", "数据集归类", "从图片中裁切人物、半身、头部和眼部。",
                Pipeline("images_three_stage_crop", "执行三分法裁切", "按所选检测阶段输出裁切图片。", "CyanTools.ViewModel.ThreeStagePipelineNode", AgentOperationRisk.WritesOutput)),
            Page("resolution_filter", "分辨率筛选", "数据集归类", "按最小宽高或像素数筛选图片。",
                Pipeline("images_filter_resolution", "筛选低分辨率图片", "删除不符合阈值的图片及同名 txt；必须确认。", "CyanTools.ViewModel.ResolutionFilterPipelineNode", AgentOperationRisk.Destructive)),
            Page("hand_integrity", "手指完整性筛选", "数据集归类", "通过手部关键点筛选手指不完整的图片。",
                Dedicated("images_filter_hand_integrity", "筛选手部完整性", "执行模型推理并输出合格/不合格图片。", AgentOperationRisk.WritesOutput)),
            Page("keep_files", "保留指定文件", "数据集归类", "只保留选定扩展名的文件。",
                Pipeline("files_keep_selected_types", "保留指定文件类型", "删除未被保留规则匹配的文件；必须确认。", "CyanTools.ViewModel.KeepFilesPipelineNode", AgentOperationRisk.Destructive)),
            Page("folder_processing", "文件夹处理", "数据集处理", "批量执行移动、清理、解压和训练输出转换等文件夹操作。",
                Pipeline("files_process_folder", "处理文件夹", "按页面选择的文件夹操作执行处理。", "CyanTools.ViewModel.FolderProcessingPipelineNode", AgentOperationRisk.Destructive)),
            Page("video_frames", "导出视频帧", "数据集处理", "按时间间隔从视频文件导出帧图像。",
                Pipeline("video_export_frames", "导出视频帧", "将视频帧写入输出目录。", "CyanTools.ViewModel.ExportVideoFramePipelineNode", AgentOperationRisk.WritesOutput)),
            Page("image_metadata", "图片元数据", "数据集处理", "读取、编辑或清除图片元数据。",
                Dedicated("images_manage_metadata", "管理图片元数据", "读取或更新图片文件元数据。", AgentOperationRisk.WritesOutput)),
            Page("gaussian_blur", "高斯模糊", "数据集处理", "批量对图片施加高斯模糊。",
                Pipeline("images_gaussian_blur", "应用高斯模糊", "将模糊后的图片写入输出目录。", "CyanTools.ViewModel.GaussianBlurPipelineNode", AgentOperationRisk.WritesOutput)),
            Page("image_clustering", "图片聚类", "数据集归类", "按内容或画风特征聚类图片。",
                Pipeline("images_cluster", "聚类图片", "提取特征并将图片导出到聚类目录。", "CyanTools.ViewModel.ImageClusteringViewModel", AgentOperationRisk.WritesOutput)),
            Page("same_source_grouping", "同源图片归类", "数据集归类", "识别重复、包含和差分关系并导出分组。",
                Pipeline("images_group_same_source", "归类同源图片", "分析输入目录并导出同源图片分组。", "CyanTools.ViewModel.SameSourceImageGroupingViewModel", AgentOperationRisk.WritesOutput)),
            Page("format_conversion", "格式转换", "数据集处理", "在 PNG、JPEG、BMP、WEBP 和 PSD 支持格式之间批量转换。",
                Pipeline("images_convert_format", "转换图片格式", "将图片转换为指定格式并写入输出目录。", "CyanTools.ViewModel.ConvertImageFormatPipelineNode", AgentOperationRisk.WritesOutput)),
            Page("transparent_channel", "转换透明通道", "数据集处理", "批量处理图片的透明通道。",
                Pipeline("images_convert_transparent_channel", "转换透明通道", "将处理后的图片写入输出目录。", "CyanTools.ViewModel.ConvertTransparentChannelPipelineNode", AgentOperationRisk.WritesOutput)),
            Page("tagging", "图片打标", "数据集处理", "使用本地标签模型为图片生成或合并标签文本。",
                Pipeline("images_tag", "为图片打标", "初始化标签模型并为输入图片写入标签。", "CyanTools.ViewModel.TaggingPipelineNode", AgentOperationRisk.WritesOutput)),
            Page("training_set_balance", "训练集配平", "训练", "按标签组合统计并配平训练集。",
                Pipeline("training_set_balance", "配平训练集", "重排训练集文件以改善标签组合分布。", "CyanTools.ViewModel.TrainingSetBalancerPipelineNode", AgentOperationRisk.WritesOutput)),
            Page("xyz", "XYZ工具", "训练", "生成区间式 XYZ 参数序列。",
                Dedicated("xyz_generate", "生成 XYZ 序列", "根据起止值和步长生成文本参数序列。", AgentOperationRisk.ReadOnly)),
            Page("image_rename", "图片重命名", "数据集处理", "预览并批量重命名图片及同名标签文件。",
                Pipeline("images_rename", "重命名图片", "按前缀和编号规则重命名文件；必须确认。", "CyanTools.ViewModel.ImageRenamePipelineNode", AgentOperationRisk.Destructive)),
            Page("dataset_downloader", "数据集下载器", "数据集获取", "从已配置的站点下载图片和标签。",
                Pipeline("dataset_download", "下载数据集", "按选择的下载来源获取图片；需要已配置的凭据。", "CyanTools.ViewModel.DatasetDownloaderPipelineNode", AgentOperationRisk.ExternalService)),
            Page("gemini_generation", "Gemini图像生成", "数据集获取", "通过已配置的 Gemini 服务生成或编辑图片。",
                Dedicated("images_generate_gemini", "生成 Gemini 图片", "向已配置的外部图片生成服务提交任务。", AgentOperationRisk.ExternalService)),
            Page("options", "选项", "环境设置", "检查和管理 GPU、CUDA、WSL 与训练后端。",
                Dedicated("environment_manage", "管理运行环境", "检查或变更本地训练运行环境；安装/删除动作必须确认。", AgentOperationRisk.EnvironmentChange))
        ]);

    public static IReadOnlyList<AgentPageDefinition> GetPages() => Pages;

    public static AgentPageDefinition GetPage(string pageId) =>
        Pages.FirstOrDefault(page => string.Equals(page.Id, pageId, StringComparison.OrdinalIgnoreCase))
        ?? throw new KeyNotFoundException($"Unknown CyanTools page id: {pageId}");

    public static AgentOperationDefinition GetOperation(string operationId) =>
        Pages.SelectMany(page => page.Operations)
            .FirstOrDefault(operation => string.Equals(operation.Id, operationId, StringComparison.OrdinalIgnoreCase))
        ?? throw new KeyNotFoundException($"Unknown CyanTools operation id: {operationId}");

    private static AgentPageDefinition Page(
        string id,
        string displayName,
        string category,
        string description,
        params AgentOperationDefinition[] operations) =>
        new(id, displayName, category, description, operations);

    private static AgentOperationDefinition Pipeline(
        string id,
        string displayName,
        string description,
        string pipelineNodeType,
        AgentOperationRisk risk) =>
        new(id, displayName, description, risk, AgentExecutionKind.PipelineNode, pipelineNodeType,
        [
            new("input_dir", "输入目录或文件所在目录；必须位于服务允许的本地目录中。", true, "string"),
            new("output_dir", "可选的输出目录；留空时沿用该工具的默认输出规则。", false, "string"),
            new("parameters", "调用 cyantools_describe_operation 后得到的页面参数 JSON 对象。", false, "object")
        ]);

    private static AgentOperationDefinition Dedicated(
        string id,
        string displayName,
        string description,
        AgentOperationRisk risk) =>
        new(id, displayName, description, risk, AgentExecutionKind.DedicatedAdapter);
}

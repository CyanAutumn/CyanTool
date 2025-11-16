using CyanTools.Model;
using System.Collections.ObjectModel;

namespace CyanTools.ViewModel
{
    public class ModelTrainingViewModel : TrainingConfigViewModelBase
    {
        protected override string DefaultConfigRelativePath => System.IO.Path.Combine("config", "default.toml");
        protected override string ViewDisplayName => "模型训练";

        protected override ObservableCollection<TrainingSection> BuildDefaultSections()
        {
            return new ObservableCollection<TrainingSection>
            {
                new TrainingSection("model", "模型设置", new ObservableCollection<TrainingParameter>
                {
                    new TrainingParameter("v2", ParameterValueKind.Boolean, "false", "v2 / 是否为 V2 模型"),
                    new TrainingParameter("v_parameterization", ParameterValueKind.Boolean, "false", "v_parameterization / v 参数化"),
                    new TrainingParameter("pretrained_model_name_or_path", ParameterValueKind.String, "./sd-models/model.ckpt", "pretrained_model_name_or_path / 预训练模型路径")
                }),
                new TrainingSection("dataset", "数据集", new ObservableCollection<TrainingParameter>
                {
                    new TrainingParameter("train_data_dir", ParameterValueKind.String, "./train/input", "train_data_dir / 训练数据目录"),
                    new TrainingParameter("reg_data_dir", ParameterValueKind.String, "", "reg_data_dir / 正则化数据目录"),
                    new TrainingParameter("prior_loss_weight", ParameterValueKind.Number, "1", "prior_loss_weight / 先验损失权重"),
                    new TrainingParameter("cache_latents", ParameterValueKind.Boolean, "true", "cache_latents / 缓存潜变量"),
                    new TrainingParameter("shuffle_caption", ParameterValueKind.Boolean, "true", "shuffle_caption / 随机标题"),
                    new TrainingParameter("enable_bucket", ParameterValueKind.Boolean, "true", "enable_bucket / 启用分桶")
                }),
                new TrainingSection("additional_network", "LoRA/网络", new ObservableCollection<TrainingParameter>
                {
                    new TrainingParameter("network_dim", ParameterValueKind.Number, "32", "network_dim / 网络维度"),
                    new TrainingParameter("network_alpha", ParameterValueKind.Number, "16", "network_alpha / 网络 Alpha"),
                    new TrainingParameter("network_train_unet_only", ParameterValueKind.Boolean, "false", "network_train_unet_only / 仅训练 UNet"),
                    new TrainingParameter("network_train_text_encoder_only", ParameterValueKind.Boolean, "false", "network_train_text_encoder_only / 仅训练文本编码器"),
                    new TrainingParameter("network_module", ParameterValueKind.String, "networks.lora", "network_module / 网络模块"),
                    new TrainingParameter("network_args", ParameterValueKind.Raw, "[]", "network_args / 额外参数")
                }),
                new TrainingSection("optimizer", "优化器", new ObservableCollection<TrainingParameter>
                {
                    new TrainingParameter("unet_lr", ParameterValueKind.Number, "1e-4", "unet_lr / UNet 学习率"),
                    new TrainingParameter("text_encoder_lr", ParameterValueKind.Number, "1e-5", "text_encoder_lr / 文本编码器学习率"),
                    new TrainingParameter("optimizer_type", ParameterValueKind.String, "AdamW8bit", "optimizer_type / 优化器类型", options: DefaultOptimizerOptions),
                    new TrainingParameter("lr_scheduler", ParameterValueKind.String, "cosine_with_restarts", "lr_scheduler / 学习率调度器", options: DefaultLrSchedulerOptions),
                    new TrainingParameter("lr_warmup_steps", ParameterValueKind.Number, "0", "lr_warmup_steps / 预热步数"),
                    new TrainingParameter("lr_restart_cycles", ParameterValueKind.Number, "1", "lr_restart_cycles / 重启周期")
                }),
                new TrainingSection("training", "训练参数", new ObservableCollection<TrainingParameter>
                {
                    new TrainingParameter("resolution", ParameterValueKind.String, "512,512", "resolution / 训练分辨率"),
                    new TrainingParameter("train_batch_size", ParameterValueKind.Number, "1", "train_batch_size / 训练批大小"),
                    new TrainingParameter("max_train_epochs", ParameterValueKind.Number, "10", "max_train_epochs / 最大训练轮数"),
                    new TrainingParameter("noise_offset", ParameterValueKind.Number, "0.0", "noise_offset / 噪声偏移"),
                    new TrainingParameter("keep_tokens", ParameterValueKind.Number, "0", "keep_tokens / 保留标记数"),
                    new TrainingParameter("xformers", ParameterValueKind.Boolean, "true", "xformers / 启用 xFormers"),
                    new TrainingParameter("lowram", ParameterValueKind.Boolean, "false", "lowram / 低内存模式"),
                    new TrainingParameter("clip_skip", ParameterValueKind.Number, "2", "clip_skip / CLIP 跳层"),
                    new TrainingParameter("mixed_precision", ParameterValueKind.String, "fp16", "mixed_precision / 混合精度"),
                    new TrainingParameter("save_precision", ParameterValueKind.String, "fp16", "save_precision / 保存精度")
                }),
                new TrainingSection("sample_prompt", "采样设置", new ObservableCollection<TrainingParameter>
                {
                    new TrainingParameter("sample_sampler", ParameterValueKind.String, "euler_a", "sample_sampler / 采样算法"),
                    new TrainingParameter("sample_every_n_epochs", ParameterValueKind.Number, "1", "sample_every_n_epochs / 采样周期")
                }),
                new TrainingSection("saving", "保存策略", new ObservableCollection<TrainingParameter>
                {
                    new TrainingParameter("output_name", ParameterValueKind.String, "output_name", "output_name / 输出名称"),
                    new TrainingParameter("save_every_n_epochs", ParameterValueKind.Number, "1", "save_every_n_epochs / 保存周期"),
                    new TrainingParameter("save_n_epoch_ratio", ParameterValueKind.Number, "0", "save_n_epoch_ratio / 保存比例"),
                    new TrainingParameter("save_last_n_epochs", ParameterValueKind.Number, "499", "save_last_n_epochs / 保留最近轮数"),
                    new TrainingParameter("save_state", ParameterValueKind.Boolean, "false", "save_state / 保存状态"),
                    new TrainingParameter("save_model_as", ParameterValueKind.String, "safetensors", "save_model_as / 保存格式"),
                    new TrainingParameter("output_dir", ParameterValueKind.String, "./output", "output_dir / 输出目录"),
                    new TrainingParameter("logging_dir", ParameterValueKind.String, "./logs", "logging_dir / 日志目录"),
                    new TrainingParameter("log_prefix", ParameterValueKind.String, "output_name", "log_prefix / 日志前缀")
                }),
                new TrainingSection("others", "其他", new ObservableCollection<TrainingParameter>
                {
                    new TrainingParameter("min_bucket_reso", ParameterValueKind.Number, "256", "min_bucket_reso / 最小分桶分辨率"),
                    new TrainingParameter("max_bucket_reso", ParameterValueKind.Number, "1024", "max_bucket_reso / 最大分桶分辨率"),
                    new TrainingParameter("caption_extension", ParameterValueKind.String, ".txt", "caption_extension / 标注文件后缀"),
                    new TrainingParameter("max_token_length", ParameterValueKind.Number, "225", "max_token_length / 最大标记长度"),
                    new TrainingParameter("seed", ParameterValueKind.Number, "1337", "seed / 随机种子")
                })
            };
        }
    }
}

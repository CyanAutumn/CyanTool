# CyanTools

CyanTools 是面向 AI 绘图与模型训练的数据集工具箱，使用 **C# / .NET 8 / WPF** 开发。它把素材获取、图片整理、同源归类、风格聚类、自动打标、训练配置、训练集版本管理和工作流调度集中到一个 Windows 桌面程序中。

界面支持简体中文、English、日本語，以及浅色 / 深色主题。常规图片处理使用本地 C# 实现；模型推理由 ONNX Runtime 执行，训练和超分按功能调用对应的外部后端。

[源代码](https://github.com/CyanAutumn/CyanTool) · [下载与更新](https://github.com/CyanAutumn/CyanToolsUpdate/releases) · [反馈问题](https://github.com/CyanAutumn/CyanTool/issues)

**许可证：GNU GPL v3（GPL-3.0-only）**。分发使用本项目代码的受许可作品时，须按 GPLv3 提供对应源代码，并保留 CyanTools 项目来源、版权及许可声明，详见 [LICENSE](LICENSE) 和 [NOTICE](NOTICE)。

## 目录

- [功能概览](#功能概览)
- [运行环境与快速开始](#运行环境与快速开始)
- [典型工作流程](#典型工作流程)
- [模型与外部工具](#模型与外部工具)
- [框架与依赖](#框架与依赖)
- [从源码构建](#从源码构建)
- [Agent 与 MCP](#agent-与-mcp)
- [目录结构与数据保存](#目录结构与数据保存)
- [开发与贡献](#开发与贡献)
- [参考项目与致谢](#参考项目与致谢)
- [常见问题](#常见问题)
- [许可证与第三方声明](#许可证与第三方声明)

## 功能概览

以下按当前源码中的导航分类整理，实际入口以程序导航为准。

### 数据集获取

| 功能 | 主要用途 |
| --- | --- |
| 数据集管理器：素材库 | 按素材目录管理 Danbooru、Pixiv、Kemono 下载配置；扫描已有素材、检查更新、续传、格式转换、图片去重、保存标签，并显示各来源进度 |
| Danbooru 手动下载器 | 独立的帖子 ID / 标签下载入口，以及基于内嵌浏览器的正则采集窗口 |
| Gemini 图像生成 | 配置 API 地址、密钥和模型，进行图像生成或带参考图片的编辑 |

素材库支持多个 Pixiv 令牌、任务取消、请求间隔和共享代理节点。下载配置按素材保存，下载图片默认进入素材的 `原图/` 子目录。

### 数据集归类

| 功能 | 主要用途 |
| --- | --- |
| 图片分辨率归类 | 按尺寸、比例和分桶规则整理图片 |
| 图片三分法 | 使用人物、半身、头部和眼部检测模型输出裁切图片 |
| 分辨率筛选 | 按宽高、像素数等条件筛选图片；独立页面和工作流中的清理会影响源文件 |
| 图片聚类 | 使用 CSD 特征、灰度 / 线条 / 头部风格特征进行聚类，支持降维、自动聚类和结果分布查看 |
| 同源图片归类 | 按“缩略图 → 副本 → 被包含 → 差分”识别原图与子图关系，输出分类目录和 `manifest.json`；当前分类计算使用 CPU |
| 美学评分 | 使用 DeepGHS 原模型和样本校准计算原始分数、百分位与七档等级，自动按等级复制图片及标签，并导出 JSON 报告 |
| 远景图检测 | 依据半身检测框的面积占比区分近景与远景，生成素材索引，支持查看与还原 |
| 保留指定文件 | 按所选扩展名保留文件，清理未匹配的文件 |

从素材库进入同源归类、分辨率归类、分辨率筛选、聚类、美学评分时，使用素材索引模式。该模式与独立工具页面的直接处理行为不同，详见[典型工作流程](#典型工作流程)。

### 数据集处理

| 功能 | 主要用途 |
| --- | --- |
| 图片超分 | 调用 Real-ESRGAN ncnn Vulkan 或 waifu2x-caffe 进行放大与降噪 |
| 图片上下采 | 按目标尺寸和面积条件批量缩放图片 |
| 图片处理 | 执行镜像等通用图片处理 |
| 数据集管理器：图片浏览与编辑 | 浏览图片与同名 TXT，编辑 Prompt、标记分类 / 删除、绘制黑框并批量保存 |
| 文件夹处理 | 批量移动、清理、解压文件，以及转换 diffusion-pipe 训练输出等 |
| 导出视频帧 | 浏览视频并导出帧图像，工作流中使用批量导出模式 |
| 图片元数据 | 查看、编辑或清理图片元数据 |
| 高斯模糊 | 按参数批量模糊图片 |
| 格式转换 | 批量转换图片格式；可从素材行跳转并返回素材库 |
| 转换透明通道 | 处理图片透明通道和背景 |
| 图片打标 | 使用 WD、DeepGHS、CL 等 ONNX 标签模型生成同名 TXT，支持阈值、标签类别和自定义模型 |
| 图片重命名 | 批量顺序命名或打乱命名，并同步处理同名标签 |

导航中有两个“数据集管理器”：**数据集获取**分类中的页面管理素材与下载，**数据集处理**分类中的页面用于图片浏览和编辑。

### 训练与工作流

| 功能 | 主要用途 |
| --- | --- |
| 工作流 | 在 Nodify 节点画布中配置处理步骤，保存、加载、重命名工作流，并按顺序执行 |
| 训练集管理器 | 将真实 Git 提交作为数据集版本，管理分支、图片 / Prompt 快照、配置比较及训练产物，大文件使用 Git LFS |
| 训练 | 在四种 Windows / WSL2 训练后端之间导入、编辑和导出 JSON、TOML、CLI 参数，保存草稿并直接启动训练 |
| LoRA 分层融合 | 按 SDXL / Kohya 25 层、Anima LoRA Block 或 Anima 大模型 Block 权重融合 Safetensors 文件 |
| 模型源数据 | 查看、编辑或清除 Safetensors 元数据，保留张量内容 |
| 训练集配平 | 统计数据集子目录并调整训练重复次数对应的目录命名 |
| LLM 工具 | 调用支持图像输入的 OpenAI 兼容服务，按自定义提示词归类图片或生成标签 |
| XYZ 工具 | 按起止值、间隔生成可复制的序号提示词 |

训练页还提供 CPU、RAM、NVIDIA GPU、显存和磁盘监控，以及可拖动的桌面悬浮窗。训练进度和 Loss 的展示取决于后端是否回传相应信息。

### 环境设置

| 功能 | 主要用途 |
| --- | --- |
| 选项 | 检查 CUDA / cuDNN、WSL 和训练后端，管理托管或自定义运行环境路径 |
| 跑图管理 | 启动 / 停止 Windows 或 WSL2 中已有的 ComfyUI、Webui Forge Neo，查看进程输出并打开模型目录；ComfyUI 另有节点目录入口 |
| 代理设置 | 管理 Clash 订阅、本地代理端口、节点选择、延迟检查和托管 Mihomo 内核 |

源码另保留了“模型训练”“LoRA 训练”“手指完整性筛选”页面，但当前被 `ToolPageRegistry` 隐藏，不属于默认可见入口。旧版训练页使用的 lora-scripts 也不等同于当前“训练”页的四后端绑定。

## 运行环境与快速开始

### 基础环境

| 场景 | 要求 |
| --- | --- |
| 桌面程序 | Windows，建议使用 Windows 10 / 11 x64；主项目目标框架为 `net8.0-windows` |
| 运行普通构建包 | [.NET 8 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/8.0)；SDK 构建产物默认不自带完整 .NET 运行时 |
| 浏览器采集 / Pixiv 授权 | [Microsoft Edge WebView2 Runtime](https://developer.microsoft.com/microsoft-edge/webview2/) |
| ONNX 模型推理 | 对应的模型文件；启用 NVIDIA GPU 推理时还需匹配所用 ONNX Runtime 的 CUDA / cuDNN 运行库 |
| Real-ESRGAN 超分 | 对应外部程序、模型和可用的 Vulkan 显卡驱动 |
| 训练集版本管理 | Windows Git、Git LFS，并能从 PATH 调用 |
| 模型训练 | 已部署的对应训练器、Python 环境和训练所需模型；WSL 后端还需要 WSL2 / Linux 环境 |
| LLM / Gemini 功能 | 可用的模型服务地址，以及该服务要求的认证配置 |

普通图片处理、美学评分和同源图片归类无需额外启动 Python 服务。训练所需 Python 环境由外部训练器或部署脚本管理。各功能的 CPU / GPU 能力独立，例如 Real-ESRGAN 的 Vulkan 环境与 ONNX Runtime 的 CUDA 环境并不通用。

### 使用发布版

1. 从[更新仓库 Releases](https://github.com/CyanAutumn/CyanToolsUpdate/releases)下载压缩包，完整解压到有写入权限的目录。
2. 保留同目录的 DLL、`runtimes/`、脚本和模型资源，运行 `CyanTools.exe`。
3. 从侧边栏或导航搜索进入功能页，选择输入目录并检查页面参数。
4. 对需要模型或外部程序的功能，先按下文准备资源；训练后端路径统一在“环境设置 → 选项”管理。

分辨率筛选、保留指定文件、重命名及数据集编辑保存等功能会删除、移动或覆盖源文件，首次使用请在数据副本上确认效果。下载站点、模型和 API 的网络访问仅在相应功能需要时使用；程序启动也会检查更新。

## 典型工作流程

### 素材下载、归类和复核

1. 打开“数据集获取 → 数据集管理器”，选择管理目录和素材目录模板，例如 `{name}_{id}`。
2. 设置来源、输出格式、标签、令牌、代理和请求间隔，点击“添加到素材库”；该操作保存素材配置，不立即下载。
3. 在素材行点击“检查”开始下载，图片默认保存到 `<素材>/原图/`。已有且可解码的同名图片可按去重设置跳过。
4. 右键素材进入同源归类、分辨率归类、筛选、聚类、美学评分或远景检测，点击“生成索引”。索引生成阶段不复制或移动原图。
5. 点击“查看”时，程序按索引移动图片与同名标签；点击“还原”或素材行的“还原原图”，恢复原始文件名和目录。一个素材同一时间只展开一份分类结果。

索引模式固定处理 `<素材>/原图/` 及其子目录；素材行选择的子文件夹用于格式转换等工具跳转。远景检测的默认半身面积阈值为 25%，最大半身框面积占比大于阈值时判为近景，其余判为远景。

查看状态会持久化。对查看中的结果进行外部改名、删除或格式转换前，应先还原，避免文件位置与恢复记录不一致。

### 可重复的数据集制备

在“训练 → 工作流”中添加节点、调整顺序、编辑参数并保存。一个常见的处理顺序为：

```text
选择输入文件夹 → 格式转换 → 转换透明通道 → 图片上下采 → 图片打标
```

节点画布是编辑界面，执行仍采用有序流水线。目录型节点的输出会按工作流规则自动生成，再传给下一步；显式输出节点可用于调整输出位置。不要把画布连线理解成任意分支、并行或循环执行。

当前支持的节点包括输入 / 输出目录选择、数据集下载器、聚类、超分、上下采、分辨率归类、三分法、分辨率筛选、保留指定文件、文件夹处理、视频帧导出、高斯模糊、格式 / 透明通道转换、同源归类、打标、训练集配平、训练和图片重命名。

数据集下载器节点在一个工作流中只能出现一次，并且必须是首节点。工作流中的分辨率筛选和保留指定文件会直接清理输入；打标原地生成 TXT，训练集配平会应用目录重命名。

### 训练与跑图环境

1. 在“选项”中部署或绑定训练器，检查程序根目录、配置目录和 WSL 发行版。
2. 打开“训练”，选择源训练器导入配置，再选择目标训练器编辑、预览或导出参数。
3. 点击“开始训练”可生成本次运行配置并启动对应后端；也可保存为草稿后继续调整。
4. 需要管理历史数据时，用“训练集管理器”维护 Git / LFS 版本及关联产物。
5. 已安装的 ComfyUI / Webui Forge Neo 可在“跑图管理”中绑定并启动。

当前训练绑定为 `sd-scripts / Windows`、`Anima Fast LoRA / Windows`、`wsl-sd-scripts / WSL2` 和 `diffusion-pipe / WSL2`。ComfyUI / Webui Forge Neo 是推理前端绑定，当前提供检测、启动和目录管理，需自行准备对应程序。

## 模型与外部工具

模型权重、大型外部仓库和发布资源多数不纳入 Git。克隆源码并还原 NuGet 包后，相关模型功能仍可能需要另外下载资源。除下文说明的聚类模型外，下列路径相对于程序目录；构建时仅会复制源码目录中实际存在、且已由项目声明包含的资源。

| 资源 | 预期位置 / 获取方式 |
| --- | --- |
| Real-ESRGAN ncnn Vulkan | `realesrgan-ncnn-vulkan-20220424-windows/realesrgan-ncnn-vulkan.exe`，保留同目录的 `models/` 和随附资源；从[上游发行版](https://github.com/xinntao/Real-ESRGAN-ncnn-vulkan/releases)获取 |
| waifu2x-caffe | `waifu2x-caffe/waifu2x-caffe-cui.exe`，保留其模型和依赖；从[上游发行版](https://github.com/lltcggie/waifu2x-caffe/releases)获取 |
| CSD 特征模型 | `Models/csd/csd_clip_model.onnx`；聚类页可下载，来源为 [yuxi-liu-wired/CSD](https://huggingface.co/yuxi-liu-wired/CSD) |
| 人物检测 | `Models/anime_person_detection/person_detect_v1.1_m.onnx` |
| 半身检测 | `Models/anime_halfbody_detection/halfbody_detect_v1.0_s.onnx`，三分法和远景检测共用 |
| 头部检测 | `Models/anime_head_detection/head_detect_v0_s.onnx`，三分法及部分风格特征使用 |
| 眼部检测 | `Models/anime_eye_detection/eye_detect_v1.0_s.onnx` |
| 打标模型与标签映射 | `TaggerModels/`；页面按选择下载 ONNX 与 CSV / JSON，也支持添加自定义模型 |
| 美学评分模型 | 默认位于 `Models/anime_aesthetic/swinv2pv3_v0_448_ls0.2_x/`，需包含指定版本的 `model.onnx`、`meta.json` 和 `samples.npz`；页面提供下载 / 修复与哈希校验 |
| 托管 Mihomo | 由“代理设置”下载、校验和管理；更新后按版本保存，运行中的任务继续使用原版本 |

人物、半身、头部和眼部模型使用 DeepGHS 对应的模型系列，来源链接见[参考项目与致谢](#参考项目与致谢)。源码中的模型文件名和版本应与实际下载文件保持一致。

聚类页的 CSD、头部和眼部模型使用相对路径，按当前工作目录解析。源码启动时在仓库根目录运行；从发布目录启动时，应让工作目录与包含 `Models/` 的目录一致。

CUDA / cuDNN 统一在“选项”中检查和管理。ONNX Runtime 的版本兼容要求请查阅[官方 CUDA 执行提供器文档](https://onnxruntime.ai/docs/execution-providers/CUDA-ExecutionProvider.html)；各页面是否回退 CPU 以页面实现和日志为准，美学评分支持自动 CPU 回退。

## 框架与依赖

以下版本来自当前项目文件，表示本仓库的依赖配置。

### 应用架构

| 层次 | 技术与作用 |
| --- | --- |
| 桌面平台 | C#、[.NET 8 / WPF](https://learn.microsoft.com/dotnet/desktop/wpf/overview/)，目标框架 `net8.0-windows` |
| 界面 | [WPF UI](https://github.com/lepoco/wpfui) 提供 Fluent 控件、导航和窗口；`Themes/` 统一页面样式 |
| 交互组织 | MVVM、数据绑定、命令及项目内的属性通知实现；业务处理集中在 `Utils/` 和各 ViewModel |
| 工作流 | [Nodify](https://github.com/miroiu/nodify) 负责节点编辑，`Framework/Pipeline/` 负责顺序执行、路径传递和工作流持久化 |
| Agent 契约 | `CyanTools.Agent` 使用 `net8.0`，定义独立于界面的页面、操作、参数和风险契约 |
| MCP 服务 | `CyanTools.McpServer` 使用官方 [MCP C# SDK](https://github.com/modelcontextprotocol/csharp-sdk)，通过 stdio 提供工具 |

### 桌面端直接 NuGet 依赖

完整声明见 [CyanTools.csproj](CyanTools.csproj)。OpenCvSharp 与 ImageSharp 的配套包在下表分别列出版本。

| 包 | 版本 | 用途 |
| --- | --- | --- |
| [WPF-UI](https://www.nuget.org/packages/WPF-UI) | 4.3.0 | Fluent 风格控件、窗口与导航 |
| [WPF-UI.Markdown](https://www.nuget.org/packages/WPF-UI.Markdown) | 4.0.2 | Markdown 内容展示 |
| [Nodify](https://www.nuget.org/packages/Nodify) | 7.3.0 | 工作流节点画布 |
| [Magick.NET-Q8-AnyCPU](https://www.nuget.org/packages/Magick.NET-Q8-AnyCPU) | 14.15.0 | 图片格式转换与处理，包括 PSD 等格式 |
| [SixLabors.ImageSharp](https://www.nuget.org/packages/SixLabors.ImageSharp) | 3.1.11 | 图片解码、缩放、特征预处理和元数据读取 |
| [SixLabors.ImageSharp.Drawing](https://www.nuget.org/packages/SixLabors.ImageSharp.Drawing) | 2.1.7 | 图片绘制 |
| [OpenCvSharp4](https://www.nuget.org/packages/OpenCvSharp4) | 4.9.0.20240103 | 视觉处理、匹配和视频读取 |
| [OpenCvSharp4.WpfExtensions](https://www.nuget.org/packages/OpenCvSharp4.WpfExtensions) | 4.9.0.20240103 | OpenCV 与 WPF 图像转换 |
| [OpenCvSharp4.runtime.win](https://www.nuget.org/packages/OpenCvSharp4.runtime.win) | 4.9.0.20240103 | OpenCV Windows 原生运行库 |
| [Microsoft.ML.OnnxRuntime.Gpu](https://www.nuget.org/packages/Microsoft.ML.OnnxRuntime.Gpu) | 1.22.0 | 检测、聚类特征、打标和美学模型推理 |
| [Microsoft.ML](https://www.nuget.org/packages/Microsoft.ML) | 4.0.2 | 机器学习相关依赖 |
| [UMAP](https://www.nuget.org/packages/UMAP) | 1.0.62830 | 特征降维相关支持 |
| [System.Numerics.Tensors](https://www.nuget.org/packages/System.Numerics.Tensors) | 9.0.9 | 张量运算支持 |
| [Microsoft.Data.Sqlite](https://www.nuget.org/packages/Microsoft.Data.Sqlite) | 8.0.28 | SQLite 数据访问 |
| [SQLitePCLRaw.bundle_e_sqlite3](https://www.nuget.org/packages/SQLitePCLRaw.bundle_e_sqlite3) | 3.0.3 | SQLite 原生运行库 |
| [Microsoft.Web.WebView2](https://www.nuget.org/packages/Microsoft.Web.WebView2) | 1.0.2792.45 | 内嵌浏览器与授权界面 |
| [Newtonsoft.Json](https://www.nuget.org/packages/Newtonsoft.Json) | 13.0.3 | JSON 配置和数据序列化 |
| [RestSharp](https://www.nuget.org/packages/RestSharp) | 112.1.0 | HTTP API 访问 |
| [YamlDotNet](https://www.nuget.org/packages/YamlDotNet) | 18.1.0 | Clash 订阅 YAML 解析 |
| [SharpCompress](https://www.nuget.org/packages/SharpCompress) | 1.0.0 | 压缩包读取与解压 |
| [System.Security.Cryptography.ProtectedData](https://www.nuget.org/packages/System.Security.Cryptography.ProtectedData) | 8.0.0 | Windows DPAPI 用户级数据保护 |
| [Microsoft-WindowsAPICodePack-Shell](https://www.nuget.org/packages/Microsoft-WindowsAPICodePack-Shell) | 1.1.5 | Windows Shell 与文件夹选择 |
| [Autoupdater.NET.Official](https://www.nuget.org/packages/Autoupdater.NET.Official) | 1.9.2 | 兼容旧版更新入口；应用另实现了 Git 更新清单流程 |

### MCP 与测试

| 组件 | 版本 / 配置 | 用途 |
| --- | --- | --- |
| [ModelContextProtocol](https://www.nuget.org/packages/ModelContextProtocol) | 1.4.1 | MCP 服务 |
| [Microsoft.Extensions.Hosting](https://www.nuget.org/packages/Microsoft.Extensions.Hosting) | 8.0.1 | 服务宿主 |
| 测试 | xUnit / Microsoft.NET.Test.Sdk | 根目录 `tests/` 中的功能测试项目 |

## 从源码构建

### 准备

安装 Windows 版 [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) 与 Git。使用 Visual Studio 时，安装支持 .NET 8 的 Visual Studio 2022 及“.NET 桌面开发”工作负载。

以下命令在 **Windows PowerShell** 中执行：

```powershell
git clone https://github.com/CyanAutumn/CyanTool.git
Set-Location .\CyanTool
dotnet restore .\CyanTools.csproj
dotnet build .\CyanTools.csproj -c Release --no-restore
dotnet run --project .\CyanTools.csproj -c Release --no-build
```

桌面端输出位于 `bin/Release/net8.0-windows/`。先构建主项目即可使用桌面程序；要使用 MCP，再单独构建服务：

```powershell
dotnet build .\CyanTools.McpServer\CyanTools.McpServer.csproj -c Release
```

服务输出位于 `CyanTools.McpServer/bin/Release/net8.0-windows/`。完整解决方案还包含 Agent、MCP 和部分测试项目，均具备本地文件时可执行 `dotnet build .\CyanTools.sln -c Release`。

模型和超分程序不会通过 NuGet 自动补齐。根目录的 `Models/`、外部参考工程、`dist/` 及大型权重被 `.gitignore` 排除；需要按前文准备相应资源。`CyanTools.csproj` 默认仅复制指定的 Windows 原生运行时资源。

### 打包与更新发布

[PublishUpdateRelease.ps1](PublishUpdateRelease.ps1) 用于维护者构建桌面端、合并 MCP 输出、生成更新包并发布到 `CyanAutumn/CyanToolsUpdate`。它会修改版本号、上传 Release 资源，并按选项更新 Git 增量清单，因此属于发布操作。

该脚本需要已认证的 GitHub CLI；压缩可使用 7-Zip 或 PowerShell `Compress-Archive`。训练后端引导资源由 [Build-TrainingBackendBootstrap.ps1](scripts/Build-TrainingBackendBootstrap.ps1) 准备，发布脚本可通过 `-SkipTrainingBackendBootstrapResources` 跳过该准备步骤。普通源码构建不需要运行发布脚本。

## Agent 与 MCP

`CyanTools.McpServer` 是按需启动的 **stdio MCP 服务**，复用现有的无界面业务入口。工具是否能执行由能力目录返回的适配状态决定。

| 工具 | 用途 |
| --- | --- |
| `cyantools_list_pages` | 发现页面、操作和能力状态 |
| `cyantools_describe_operation` | 读取准确参数、默认值、副作用和风险 |
| `cyantools_run_pipeline_operation` | 执行已有无界面流水线入口的操作 |
| `cyantools_list_workflows` | 列出本地保存的工作流 |
| `cyantools_describe_workflow` | 查看节点顺序、风险和首节点可覆盖参数 |
| `cyantools_run_workflow` | 按保存值执行工作流，并可覆盖首节点允许的参数 |
| `cyantools_get_safety_guide` | 查看目录授权、确认和凭据规则 |

### 注册发布版 MCP

在含有 `CyanTools.McpServer.exe` 的发布目录执行。先将示例替换为用户明确允许读写、且已经存在的具体目录：

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\Install-CyanToolsMcp.ps1 `
  -AllowedRoot 'D:\datasets','D:\outputs' -UserConfirmed
```

脚本会修改当前 Windows 用户的 Codex MCP 配置，将 `cyantools` 指向当前发布目录的服务程序，并设置 `CYANTOOLS_MCP_ALLOWED_ROOTS`。安装后重启或刷新客户端，再调用 `cyantools_list_pages` 验证。已有同名配置默认不会覆盖。

源码构建的服务也可以直接配置为 MCP 客户端的启动命令。以下示例设置当前 PowerShell 会话的目录白名单并启动服务：

```powershell
$env:CYANTOOLS_MCP_ALLOWED_ROOTS = 'D:\datasets;D:\outputs'
dotnet run --project .\CyanTools.McpServer\CyanTools.McpServer.csproj -c Release --no-build
```

直接运行时进程会等待客户端通过标准输入发送协议消息。只查看目录或参数可用以下诊断命令：

```powershell
dotnet run --project .\CyanTools.McpServer\CyanTools.McpServer.csproj -c Release --no-build -- --list
dotnet run --project .\CyanTools.McpServer\CyanTools.McpServer.csproj -c Release --no-build -- --describe images_convert_format
```

文件操作必须落在白名单内；未配置白名单时文件操作被拒绝。删除、重命名或覆盖等破坏性操作需要针对本次操作的明确确认。标记为 `dedicated_adapter_required` 的页面目前只提供能力契约，还不能通过 MCP 执行。参数名称以客户端实际工具 Schema 和 describe 结果为准。


## 目录结构与数据保存

### 源码结构

```text
CyanTools/
├─ App.xaml / MainWindow.xaml       应用入口、主窗口与导航
├─ *View.xaml / *.xaml.cs           工具页面与交互
├─ ViewModel/                      页面状态、命令和工作流节点
├─ Model/                          数据模型
├─ Utils/                          图像、下载、训练、存储及进程服务
├─ Framework/
│  ├─ Navigation/                  页面发现、分类和缓存
│  └─ Pipeline/                    节点目录、顺序执行和工作流持久化
├─ Themes/                         主题与共享样式
├─ Localization/                   简体中文、英语、日语资源与绑定
├─ CyanTools.Agent/                Agent 能力契约
├─ CyanTools.McpServer/            stdio MCP 服务
├─ scripts/                        环境部署、MCP 安装与预览脚本
├─ ThirdParty/DeepGHSImgutils/      美学评分原始来源与上游许可证
├─ LICENSE / NOTICE                项目 GPLv3 许可证与来源保留要求
├─ tests/                          根目录测试；新增文件按仓库规则忽略
└─ PublishUpdateRelease.ps1         发布与更新包脚本
```


### 运行数据

“程序目录”指运行中的可执行文件所在目录，源码构建时通常是 `bin/Release/net8.0-windows/`。下表列出主要位置，具体工具也可能使用用户选择的输出路径。

| 位置 | 内容 |
| --- | --- |
| 程序目录 `Configs/Workflow/` | 已保存工作流 JSON |
| 程序目录 `Configs/LayeredLoraMerge/` | LoRA 分层融合预设 |
| 程序目录 `Config/` | 聚类、下载器、代理等功能设置；`Config/proxy_settings.json` 保存代理配置 |
| 程序目录 `Configs/Crawler/` | 下载器凭据相关本地状态 |
| 程序目录 `config/GeminiPic/` | Gemini 功能配置 |
| 程序目录 `Models/`、`TaggerModels/` | 检测、特征、美学及标签模型 |
| 程序目录 `output/` | 部分工具的默认输出 |
| `%LOCALAPPDATA%/CyanTools/user-control-input-state.json` | 可恢复的普通页面输入，不包含运行进度和敏感字段 |
| `%LOCALAPPDATA%/CyanTools/training-backends/` | 托管训练后端、路径绑定和部署状态 |
| `%LOCALAPPDATA%/CyanTools/training-bridge/projects/` | 训练配置草稿 |
| `%LOCALAPPDATA%/CyanTools/training-bridge/runs/` | 直接训练时生成的独立运行配置 |
| `%LOCALAPPDATA%/CyanTools/desktop-widgets.json` | 系统监控悬浮窗状态和位置 |
| `<素材>/config/dataset-downloader.json` | 素材下载参数及凭据 / 节点引用 |
| `<素材>/config/material-view.json` | 素材索引查看与恢复记录 |
| `<素材>/<分类功能>/index.json` | 素材分类索引 |
| `<训练集仓库>/.git/cyantools/` | 仓库全局训练集信息、配置及恢复记录 |

代理订阅和完整节点配置采用 Windows DPAPI `CurrentUser` 保护；素材配置保存凭据引用。这些用户绑定的数据迁移到另一台电脑或另一 Windows 用户后，需要重新配置。API 配置、令牌和 Cookie 应作为本机数据管理，勿随工作流、日志或仓库公开分享。

## 开发与贡献

### 测试与贡献

按修改范围选择已有测试项目，例如在仓库根目录运行：

```powershell
dotnet test .\tests\CyanTools.Mcp.Tests.csproj -c Release
dotnet test .\tests\CyanTools.Localization.Tests.csproj -c Release
```

新增、移动或修改测试时：测试统一放在根目录 `tests/`，保持根 `.gitignore` 中的 `/tests/` 规则。部分历史测试已经跟踪，新增的本地测试不会因放入此目录而自动纳入版本控制。

问题反馈请包含程序版本、功能入口、复现步骤和去除凭据后的错误信息。

## 参考项目与致谢

感谢以下项目、模型作者和维护者。表中说明各项目在 CyanTools 中的实际用途；参考工程、外部可执行程序、训练后端与模型权重分别采用各自的集成方式。

### 实现参考与移植

| 项目 | 在 CyanTools 中的用途与来源依据 |
| --- | --- |
| [sdbds/CSD_image2embedding](https://github.com/sdbds/CSD_image2embedding) | 图片特征提取、降维和聚类流程的参考；CyanTools 使用 C# / ONNX Runtime 实现并扩展灰度、线条及头部风格特征 |
| [deepghs/imgutils](https://github.com/deepghs/imgutils) | 美学评分直接按原仓库的 `generic/classify.py` 与 `metrics/dbaesthetic.py` 移植，使用模型原始概率、`samples.npz` 百分位校准和原等级阈值；固定来源版本及 MIT 许可见 [NOTICE](ThirdParty/DeepGHSImgutils/NOTICE.md) |
| [Akegarasu/lora-scripts](https://github.com/Akegarasu/lora-scripts) | 源码中保留的旧版模型 / LoRA 训练页面的配置与启动目标；当前默认训练入口使用独立的后端绑定体系 |
| [nagadomi/waifu2x](https://github.com/nagadomi/waifu2x) | 二次元图片超分的相关参考项目；CyanTools 实际调用的 waifu2x 程序为下表的 waifu2x-caffe |


### 外部程序、训练后端与运行支持

| 项目 | 集成用途 |
| --- | --- |
| [xinntao/Real-ESRGAN-ncnn-vulkan](https://github.com/xinntao/Real-ESRGAN-ncnn-vulkan) | 图片超分的外部命令行程序，使用 ncnn / Vulkan |
| [lltcggie/waifu2x-caffe](https://github.com/lltcggie/waifu2x-caffe) | 图片超分与降噪的外部命令行程序 |
| [kohya-ss/sd-scripts](https://github.com/kohya-ss/sd-scripts) | Windows 与 WSL sd-scripts 训练后端 |
| [GALIAIS/LoraHub](https://github.com/GALIAIS/LoraHub) | Anima Fast LoRA 部署来源，使用其 `external/anima_lora` 训练器 |
| [tdrussell/diffusion-pipe](https://github.com/tdrussell/diffusion-pipe) | WSL diffusion-pipe 训练后端与相关输出格式 |
| [ComfyUI](https://github.com/Comfy-Org/ComfyUI) | 跑图管理兼容的外部推理前端之一 |
| Webui Forge Neo | 跑图管理兼容的已有推理前端，通过本地 / WSL 路径绑定启动 |
| [MetaCubeX/mihomo](https://github.com/MetaCubeX/mihomo) | Clash 订阅节点使用的托管代理内核 |
| [astral-sh/uv](https://github.com/astral-sh/uv) | 训练后端部署及引导资源准备中的 Python 环境工具 |
| [Git](https://git-scm.com/) / [Git LFS](https://git-lfs.com/) | 训练集版本历史和大文件管理 |
| [LM Studio](https://lmstudio.ai/) 等 OpenAI 兼容服务 | LLM 工具可连接的外部视觉语言模型服务 |

界面与基础能力还依赖 WPF UI、Nodify、ImageSharp、Magick.NET、OpenCvSharp、ONNX Runtime、MCP C# SDK 等项目，具体版本与用途见[框架与依赖](#框架与依赖)。

### 模型与标签数据

| 来源 | 用途 |
| --- | --- |
| [yuxi-liu-wired/CSD](https://huggingface.co/yuxi-liu-wired/CSD) | 图片内容 / 风格特征模型 |
| [SmilingWolf](https://huggingface.co/SmilingWolf) 的 WD Tagger 系列 | 自动标签推理与标签映射 |
| [DeepGHS](https://huggingface.co/deepghs) 的 anime person / halfbody / head / eye detection 系列 | 人物区域检测、三分法裁切、风格区域选择和远景检测 |
| [deepghs/anime_aesthetic](https://huggingface.co/deepghs/anime_aesthetic) | 美学评分模型与原始校准样本，固定变体 `swinv2pv3_v0_448_ls0.2_x`；版本和校验值见 [NOTICE](ThirdParty/DeepGHSImgutils/NOTICE.md) |
| [deepghs/idolsankaku-swinv2-tagger-v1](https://huggingface.co/deepghs/idolsankaku-swinv2-tagger-v1) / [idolsankaku-eva02-large-tagger-v1](https://huggingface.co/deepghs/idolsankaku-eva02-large-tagger-v1) | 页面提供的其他标签模型 |
| [cella110n/cl_tagger](https://huggingface.co/cella110n/cl_tagger) | CL 标签模型及 JSON 标签映射 |
| [toyxyz/danbooru2024_wildcard](https://huggingface.co/datasets/toyxyz/danbooru2024_wildcard) | 按类别组织的标签词典数据 |
| [deepghs/anime_hand_detection](https://huggingface.co/deepghs/anime_hand_detection) / [OpenCV MediaPipe handpose](https://huggingface.co/opencv/handpose_estimation_mediapipe) | 源码中保留的手部完整性实验页面使用的检测与关键点模型 |

## 常见问题

### 为什么克隆后能编译，但部分功能提示缺少模型或程序？

NuGet 还原只安装 .NET 依赖。模型权重、Real-ESRGAN、waifu2x-caffe 和训练器需要按[模型与外部工具](#模型与外部工具)准备，或使用对应页面提供的下载 / 部署操作。不要把本地参考目录名当成运行时程序目录名。

### 为什么启用 GPU 后仍使用 CPU？

先检查“选项”中的驱动、CUDA / cuDNN 状态及页面日志。部分推理页在 GPU 不可用时会回退 CPU；“同源图片归类”当前本就使用 CPU。外部超分和训练器需要分别检查自己的环境。

### 为什么会出现两个“数据集管理器”？

“数据集获取”下的页面管理素材、来源和下载，“数据集处理”下的页面浏览与编辑图片及 Prompt。可通过导航分类和页面说明区分。

### 分类结果查看后，原图目录中的图片去了哪里？

素材索引的“查看”会按记录移动图片及同名标签。用相同工具的“还原”或素材行的“还原原图”恢复。离开页面或重启程序不会自动还原；外部改名或移动文件可能导致恢复提示缺失路径。

### 远景检测失败时如何定位？

检查页面错误中的失败阶段、`原图/` 目录和半身模型。完整异常保存在 `%LOCALAPPDATA%/CyanTools/logs/far-scene-last-error.txt`。

### MCP 能列出页面，为什么不能执行？

查看 describe 返回的适配状态。`dedicated_adapter_required` 表示尚未开放无界面执行。已适配操作仍需满足目录白名单、模型 / 后端资源和本次操作确认要求；注册服务后还需让客户端重新加载配置。

## 许可证与第三方声明

本项目的原创代码采用 **GNU General Public License v3.0 only（GPL-3.0-only）**，许可证全文见 [LICENSE](LICENSE)，项目来源及版权声明见 [NOTICE](NOTICE)。

对外分发包含本项目代码的受许可程序或衍生作品时，需要：

- 按 GPLv3 向接收者提供对应源代码，并按 GPLv3 许可受覆盖的衍生作品。
- 保留已有版权、许可证和修改声明，同时附带 GPLv3 许可证文本。
- 按 `NOTICE` 中依据 GPLv3 第 7(b) 条设定的合理署名要求，保留 **CyanTools** 项目名及[原项目地址](https://github.com/CyanAutumn/CyanTools)。可放在随附的 `NOTICE`、第三方声明或适当法律声明中。

这些要求针对受许可代码及其分发。个人私用、内部修改和单纯使用程序生成图片，不要求公开自己的内容；GPLv3 不要求所有未对外分发的修改都公开，参见 [GNU 官方说明](https://www.gnu.org/licenses/gpl-faq.en.html#GPLRequireSourcePostedPublic)。

第三方组件、外部程序、模型与数据仍保留各自的上游许可条件。单独标明其他许可证的文件保留该声明，本项目许可不替换第三方许可。

美学评分核心直接按 DeepGHS 原仓库代码实现，保留其上游 MIT 来源声明和许可证：

- [DeepGHS 原始来源与移植说明](ThirdParty/DeepGHSImgutils/NOTICE.md)
- [DeepGHS / imgutils 上游 MIT 许可证副本](ThirdParty/DeepGHSImgutils/LICENSE)

上游原始部分保留 MIT 声明；CyanTools 原创的 C# 移植与集成部分遵循本项目 GPLv3 许可。

模型权重单独下载，许可信息以对应模型卡和所用版本为准。其他外部程序及训练器的许可证请查阅其上游仓库和随附文件。

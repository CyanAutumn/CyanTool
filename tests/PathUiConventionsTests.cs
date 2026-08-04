using CyanTools.Utils;
using System.Text.RegularExpressions;
using Xunit;

namespace CyanTools.Tests;

public sealed class PathUiConventionsTests
{
    private static readonly string[] DatasetPathXamlFiles =
    {
        "ConvertImageFormatView.xaml",
        "ConvertTransparentChannelView.xaml",
        "GaussianBlurView.xaml",
        "HandIntegrityFilterView.xaml",
        "ImageBucketingView.xaml",
        "ImageClusteringView.xaml",
        "ImageProcessingView.xaml",
        "ImageRenameView.xaml",
        "ImageScalingView.xaml",
        "KeepFilesView.xaml",
        "RealEsrganView.xaml",
        "ResolutionFilterView.xaml",
        "SameSourceImageGroupingView.xaml",
        "TaggingView.xaml",
        "ThreeStageView.xaml",
        "TrainingSetBalancerView.xaml"
    };

    private static readonly string[] BannedPathTerms =
    {
        "输入文件夹",
        "输出文件夹",
        "输入目录",
        "输出目录",
        "选择文件夹",
        "目标文件夹",
        "图片文件夹路径",
        "文件夹选择",
        "文件夹设置"
    };

    private static readonly string[] PathPickerXamlFiles =
    {
        "ConvertImageFormatView.xaml",
        "ConvertTransparentChannelView.xaml",
        "GaussianBlurView.xaml",
        "HandIntegrityFilterView.xaml",
        "ImageBucketingView.xaml",
        "ImageClusteringView.xaml",
        "ImageProcessingView.xaml",
        "ImageRenameView.xaml",
        "ImageScalingView.xaml",
        "KeepFilesView.xaml",
        "RealEsrganView.xaml",
        "ResolutionFilterView.xaml",
        "SameSourceImageGroupingView.xaml",
        "TaggingView.xaml",
        "ThreeStageView.xaml",
        "TrainingSetBalancerView.xaml"
    };

    private static readonly string[] PipelineInheritedInputViewXamlFiles =
    {
        "ConvertImageFormatView.xaml",
        "ConvertTransparentChannelView.xaml",
        "GaussianBlurView.xaml",
        "ImageRenameView.xaml",
        "ImageBucketingView.xaml",
        "ImageClusteringView.xaml",
        "ImageScalingView.xaml",
        "KeepFilesView.xaml",
        "RealEsrganView.xaml",
        "ResolutionFilterView.xaml",
        "SameSourceImageGroupingView.xaml",
        "TaggingView.xaml",
        "TrainingSetBalancerView.xaml",
        "ThreeStageView.xaml"
    };

    private static readonly string[] PipelineInheritedInputViewModelFiles =
    {
        Path.Combine("ViewModel", "ConvertImageFormatViewModel.cs"),
        Path.Combine("ViewModel", "ConvertTransparentChannelViewModel.cs"),
        Path.Combine("ViewModel", "GaussianBlurViewModel.cs"),
        Path.Combine("ViewModel", "ImageRenameViewModel.cs"),
        Path.Combine("ViewModel", "ImageBucketingViewModel.cs"),
        Path.Combine("ViewModel", "ImageClusteringViewModel.cs"),
        Path.Combine("ViewModel", "ImageScalingViewModel.cs"),
        Path.Combine("ViewModel", "KeepFilesViewModel.cs"),
        Path.Combine("ViewModel", "RealEsrganViewModel.cs"),
        Path.Combine("ViewModel", "ResolutionFilterViewModel.cs"),
        Path.Combine("ViewModel", "SameSourceImageGroupingViewModel.cs"),
        Path.Combine("ViewModel", "TaggingViewModel.cs"),
        Path.Combine("ViewModel", "TrainingSet", "TrainingSetBalancerViewModel.cs"),
        Path.Combine("ViewModel", "ThreeStageViewModel.cs")
    };

    private static readonly string[] ClearInputDirectoryAfterCompletionViewXamlFiles =
    {
        "ConvertImageFormatView.xaml",
        "ConvertTransparentChannelView.xaml",
        "GaussianBlurView.xaml",
        "HandIntegrityFilterView.xaml",
        "ImageBucketingView.xaml",
        "ImageClusteringView.xaml",
        "ImageScalingView.xaml",
        "RealEsrganView.xaml",
        "SameSourceImageGroupingView.xaml",
        "ThreeStageView.xaml"
    };

    private static readonly string[] ClearInputDirectoryAfterCompletionViewModelFiles =
    {
        Path.Combine("ViewModel", "ConvertImageFormatViewModel.cs"),
        Path.Combine("ViewModel", "ConvertTransparentChannelViewModel.cs"),
        Path.Combine("ViewModel", "GaussianBlurViewModel.cs"),
        Path.Combine("ViewModel", "HandIntegrityFilterViewModel.cs"),
        Path.Combine("ViewModel", "ImageBucketingViewModel.cs"),
        Path.Combine("ViewModel", "ImageClusteringViewModel.cs"),
        Path.Combine("ViewModel", "ImageScalingViewModel.cs"),
        Path.Combine("ViewModel", "RealEsrganViewModel.cs"),
        Path.Combine("ViewModel", "SameSourceImageGroupingViewModel.cs"),
        Path.Combine("ViewModel", "ThreeStageViewModel.cs")
    };

    private static readonly string[] BannedPathButtonTexts =
    {
        "Content=\"浏览...\"",
        "Content=\"选择...\"",
        "Content=\"选择文件夹\"",
        "Content=\"浏览\""
    };

    [Fact]
    public void DatasetPathPages_DoNotUseOldFolderTerminology()
    {
        foreach (string relativePath in DatasetPathXamlFiles)
        {
            string content = ReadRepoFile(relativePath);
            foreach (string bannedTerm in BannedPathTerms)
            {
                Assert.DoesNotContain(bannedTerm, content);
            }
        }
    }

    [Fact]
    public void PathPickerButtons_UseEllipsisButtonText()
    {
        foreach (string relativePath in PathPickerXamlFiles)
        {
            string content = ReadRepoFile(relativePath);
            foreach (string bannedButtonText in BannedPathButtonTexts)
            {
                Assert.DoesNotContain(bannedButtonText, content);
            }
        }
    }

    [Fact]
    public void ResolutionFilter_HasPathPickerButton()
    {
        string content = ReadRepoFile("ResolutionFilterView.xaml");

        Assert.Contains("Content=\"...\"", content);
        Assert.Contains("Command=\"{Binding BrowseInputCommand}\"", content);
    }

    [Fact]
    public void TrainingSetBalancer_PathTextBoxIsEditableAndHasPickerButton()
    {
        string content = ReadRepoFile("TrainingSetBalancerView.xaml");
        Match selectedFolderPathTextBox = Regex.Match(
            content,
            "<ui:TextBox[^>]*SelectedFolderPath[^>]*/>",
            RegexOptions.Singleline);

        Assert.True(selectedFolderPathTextBox.Success);
        Assert.DoesNotContain("IsReadOnly=\"True\"", selectedFolderPathTextBox.Value);
        Assert.Contains("Content=\"...\"", content);
    }

    [Fact]
    public void ExportVideoFrame_KeepsSpecificVideoLabel()
    {
        string content = ReadRepoFile("ExportVideoFrameView.xaml");

        Assert.Contains("视频路径:", content);
        Assert.Contains("选择视频", content);
    }

    [Fact]
    public void HandIntegrityFilterViewModel_UsesSharedDefaultOutputPathLogic()
    {
        string content = ReadRepoFile(Path.Combine("ViewModel", "HandIntegrityFilterViewModel.cs"));

        Assert.Contains("ToolOutputDirectoryDefaults.RefreshForInputChange", content);
        Assert.Contains("ToolOutputDirectoryDefaults.FillWhenEmpty", content);
        Assert.DoesNotContain("Path.Combine(baseDir, \"output\", \"bad_hands\")", content);
        Assert.DoesNotContain("Path.Combine(baseDir, \"output\", \"good_hands\")", content);
    }

    [Fact]
    public void RealEsrgan_OutputPathRowSpansAllThreeColumns()
    {
        string content = ReadRepoFile("RealEsrganView.xaml");

        Assert.Contains("<Grid Grid.Row=\"2\" Grid.Column=\"0\" Grid.ColumnSpan=\"3\" Margin=\"0\">", content);
    }

    [Fact]
    public void ImageProcessing_PathRowHasRootColumnDefinitions()
    {
        string content = ReadRepoFile("ImageProcessingView.xaml");
        Assert.Matches(
            new Regex(
                "<Grid Margin=\"10\">\\s*<Grid.RowDefinitions>.*?</Grid.RowDefinitions>\\s*<Grid.ColumnDefinitions>\\s*<ColumnDefinition Width=\"Auto\"/>\\s*<ColumnDefinition Width=\"\\*\"/>\\s*</Grid.ColumnDefinitions>",
                RegexOptions.Singleline),
            content);
        Assert.Contains("<Grid Grid.Row=\"0\" Grid.Column=\"1\">", content);
    }

    [Fact]
    public void TrainingSetViewModels_DoNotKeepOldInputOutputFolderPromptText()
    {
        string[] relativePaths =
        {
            Path.Combine("ViewModel", "GaussianBlurViewModel.cs"),
            Path.Combine("ViewModel", "ImageBucketingViewModel.cs"),
            Path.Combine("ViewModel", "ImageClusteringViewModel.cs"),
            Path.Combine("ViewModel", "KeepFilesViewModel.cs"),
            Path.Combine("ViewModel", "SameSourceImageGroupingViewModel.cs"),
            Path.Combine("ViewModel", "ThreeStageViewModel.cs"),
            Path.Combine("ViewModel", "TrainingSet", "TrainingSetBalancerViewModel.cs")
        };

        foreach (string relativePath in relativePaths)
        {
            string content = ReadRepoFile(relativePath);
            Assert.DoesNotContain("输入文件夹", content);
            Assert.DoesNotContain("输出文件夹", content);
            Assert.DoesNotContain("输入目录", content);
            Assert.DoesNotContain("输出目录", content);
            Assert.DoesNotContain("有效目录", content);
            Assert.DoesNotContain("有效的文件夹", content);
            Assert.DoesNotContain("开始处理文件夹", content);
        }
    }

    [Fact]
    public void PipelineDirectoryContract_ExposesInheritedInputLockFlag()
    {
        string interfaceContent = ReadRepoFile(Path.Combine("Framework", "Pipeline", "IPipelineDirectoryNode.cs"));

        Assert.Contains("bool HasInheritedInputPath { get; set; }", interfaceContent);

        foreach (string relativePath in PipelineInheritedInputViewModelFiles)
        {
            string content = ReadRepoFile(relativePath);
            Assert.Contains("HasInheritedInputPath", content);
        }
    }

    [Fact]
    public void PipelineDirectoryViews_LockInheritedInputPathInWorkflowUi()
    {
        foreach (string relativePath in PipelineInheritedInputViewXamlFiles)
        {
            string content = ReadRepoFile(relativePath);
            Assert.Contains("HasInheritedInputPath", content);
            Assert.Contains("上一个节点", content);
        }
    }

    [Fact]
    public void FolderProcessingView_LogTextBindingIsOneWay()
    {
        string content = ReadRepoFile("FolderProcessingView.xaml");

        Match logTextBox = Regex.Match(
            content,
            "<ui:TextBox[^>]*Text=\"\\{Binding LogText[^\\\"]*\\}\"[^>]*/>",
            RegexOptions.Singleline);

        Assert.True(logTextBox.Success);
        Assert.Contains("Mode=OneWay", logTextBox.Value);
    }

    [Fact]
    public void TrainingSetManagerView_ReadOnlyProjectPathBindingIsOneWay()
    {
        string content = ReadRepoFile("TrainingSetManagerView.xaml");

        Assert.Contains(
            "Text=\"{Binding ProjectRootPath, Mode=OneWay}\"",
            content);
    }

    [Fact]
    public void SimplePipelineNode_ForwardsInnerPropertyChangedEvents()
    {
        string content = ReadRepoFile(Path.Combine("Framework", "Pipeline", "SimplePipelineNode.cs"));

        Assert.Contains("INotifyPropertyChanged", content);
        Assert.Contains("notifyPropertyChanged.PropertyChanged +=", content);
        Assert.Contains("OnPropertyChanged(e.PropertyName)", content);
    }

    [Fact]
    public void ViewsWithOutputPath_ExposeClearInputDirectoryAfterCompletionOption()
    {
        foreach (string relativePath in ClearInputDirectoryAfterCompletionViewXamlFiles)
        {
            string content = ReadRepoFile(relativePath);
            Assert.Contains("执行完成后清空输入路径文件夹", content);
            Assert.Contains("ShouldClearInputDirectoryAfterCompletion", content);
        }
    }

    [Fact]
    public void ViewsWithOutputPath_ViewModelsExposeClearInputDirectoryAfterCompletionProperty()
    {
        foreach (string relativePath in ClearInputDirectoryAfterCompletionViewModelFiles)
        {
            string content = ReadRepoFile(relativePath);
            Assert.Contains("ShouldClearInputDirectoryAfterCompletion", content);
        }
    }

    [Fact]
    public void DatasetDownloaderView_PlacesStatisticsAndLogBelowOutputExecution()
    {
        string content = ReadRepoFile("DanbooruCrawlerView.xaml");

        Assert.Contains("DanbooruDownloadSummary", content);
        Assert.Contains("PixivDownloadSummary", content);
        Assert.Contains("KemonoDownloadSummary", content);
        Assert.Contains("OutputDirectoryImageTotalSummary", content);
        Assert.Contains("DownloadIntervalSeconds", content);
        Assert.Contains("图片、附件、分页和补充内容", content);
        Assert.DoesNotContain("PixivDownloadIntervalSeconds", content);
        Assert.Contains("<GroupBox Grid.Row=\"4\"", content);
        Assert.Contains("Header=\"下载日志\"", content);
        Assert.Contains("Text=\"{Binding LogText, Mode=OneWay}\"", content);
        Assert.DoesNotContain("<GridSplitter", content);
    }

    [Fact]
    public void DatasetDownloaderViewModel_ReportsPerSourceLiveTotalsAndOutputDirectoryTotal()
    {
        string content = ReadRepoFile(Path.Combine("ViewModel", "DatasetDownloaderViewModel.cs"));

        Assert.Contains("DanbooruDownloadSummary", content);
        Assert.Contains("PixivDownloadSummary", content);
        Assert.Contains("KemonoDownloadSummary", content);
        Assert.Contains("OutputDirectoryImageTotalSummary", content);
        Assert.Contains("BuildDownloadSummary", content);
        Assert.Contains("BuildOutputDirectoryImageTotalSummary", content);
        Assert.Contains("CountDownloadedImages", content);
        Assert.Contains("FetchDanbooruPostCountAsync", content);
        Assert.Contains("BuildPixivDownloadPlanAsync", content);
        Assert.Contains("BuildKemonoDownloadPlanAsync", content);
        Assert.Contains("WaitForNextRemoteRequestAsync", content);
        Assert.Contains("ResetRemoteRequestPacing", content);
        Assert.Contains("DownloadIntervalSeconds", content);
        Assert.DoesNotContain("DanbooruDownloadDelay", content);
        Assert.DoesNotContain("KemonoDownloadDelay", content);
        Assert.Contains("已处理", content);
        Assert.Contains("正在获取下载清单", content);
        Assert.Contains("本次新增", content);
        Assert.Contains("输出目录总计", content);
        Assert.Contains("DownloadOutcome.Saved", content);
        Assert.Contains("DownloadOutcome.Skipped", content);
    }

    [Fact]
    public void DatasetDownloader_RunsSourcesInParallelWithIndependentRequestPacing()
    {
        string content = ReadRepoFile(Path.Combine("ViewModel", "DatasetDownloaderViewModel.cs"));

        Assert.Contains("Task.WhenAll(tasks)", content);
        Assert.Contains("RunSourceIndependentlyAsync", content);
        Assert.Contains("_danbooruRequestPacer", content);
        Assert.Contains("_pixivRequestPacer", content);
        Assert.Contains("_kemonoRequestPacer", content);
        Assert.Contains("WaitForNextRemoteRequestAsync(_danbooruRequestPacer, token)", content);
        Assert.Contains("WaitForNextRemoteRequestAsync(_pixivRequestPacer, token)", content);
        Assert.Contains("WaitForNextRemoteRequestAsync(_kemonoRequestPacer, token)", content);
        Assert.DoesNotContain("_remoteRequestPacingLock", content);
        Assert.DoesNotContain("_nextRemoteRequestUtc", content);
    }

    [Fact]
    public void DatasetDownloader_ResolvesArtistNameOnceWithoutSerializingSourceDiscovery()
    {
        string content = ReadRepoFile(Path.Combine("ViewModel", "DatasetDownloaderViewModel.cs"));

        Assert.Contains("Task<string?> artistTagTask = ResolveSessionArtistTagAsync(input, token);", content);
        Assert.Contains("Task sourceTasks = RunSelectedTasksAsync(input, artistTagTask, token);", content);
        Assert.Contains("await Task.WhenAll(sourceTasks, artistTagTask);", content);
        Assert.Contains("_sessionOutputArtistSegment = SanitizePathSegment(artistTag ?? string.Empty, \"unknown_artist\");", content);
        Assert.Contains("var artistTag = await artistTagTask;", content);
        Assert.DoesNotContain("_sessionOutputArtistSegment = SanitizePathSegment(input", content);
        Assert.Equal(1, content.Split("ResolveDanbooruArtistTagByPixivUserIdAsync(input, token)").Length - 1);

        int sourceStartup = content.IndexOf(
            "Task sourceTasks = RunSelectedTasksAsync(input, artistTagTask, token);",
            StringComparison.Ordinal);
        int outputNameWait = content.IndexOf("await Task.WhenAll(sourceTasks, artistTagTask);", sourceStartup, StringComparison.Ordinal);
        int pixivDiscovery = content.IndexOf("var downloadPlan = await BuildPixivDownloadPlanAsync", StringComparison.Ordinal);
        int pixivOutputWait = content.IndexOf("await artistTagTask;", pixivDiscovery, StringComparison.Ordinal);
        int kemonoDiscovery = content.IndexOf("var downloadPlan = await BuildKemonoDownloadPlanAsync", StringComparison.Ordinal);
        int kemonoOutputWait = content.IndexOf("await artistTagTask;", kemonoDiscovery, StringComparison.Ordinal);

        Assert.True(sourceStartup >= 0 && outputNameWait > sourceStartup);
        Assert.True(pixivDiscovery >= 0 && pixivOutputWait > pixivDiscovery);
        Assert.True(kemonoDiscovery >= 0 && kemonoOutputWait > kemonoDiscovery);
    }

    [Fact]
    public void DatasetDownloader_RetriesArtistLookupAndDoesNotReportRequestFailuresAsMissingArtists()
    {
        string content = ReadRepoFile(Path.Combine("ViewModel", "DatasetDownloaderViewModel.cs"));

        Assert.Contains("private const int DanbooruArtistLookupRetryCount = 3;", content);
        Assert.Contains("new RestRequest(\"/artists.json\", Method.Get)", content);
        Assert.Contains(".AddQueryParameter(\"search[url_matches]\", pattern)", content);
        Assert.Contains(".AddQueryParameter(\"limit\", 1)", content);
        Assert.Contains("artist 标签查询失败（第 {attempt} 次），将重试", content);
        Assert.Contains("Danbooru artist 标签查询失败（已尝试 {DanbooruArtistLookupRetryCount} 次）", content);
        Assert.DoesNotContain("/artists.json?search[url_matches]", content);
    }

    [Fact]
    public void WorkflowLog_IsCopyableContinuousText()
    {
        string view = ReadRepoFile("PipelineSchedulerView.xaml");
        string viewModel = ReadRepoFile(Path.Combine("ViewModel", "PipelineSchedulerViewModel.cs"));

        Assert.Contains("Text=\"{Binding LogText, Mode=OneWay}\"", view);
        Assert.Contains("Style=\"{StaticResource CyanLogTextBoxStyle}\"", view);
        Assert.DoesNotContain("ItemsSource=\"{Binding LogLines}\"", view);
        Assert.Contains("<Grid Height=\"220\"", view);
        Assert.DoesNotContain("<Border Height=\"145\"", view);
        Assert.DoesNotContain("工作流执行输出", view);
        Assert.Contains("public string LogText", viewModel);
        Assert.Contains("Environment.NewLine", viewModel);
        Assert.DoesNotContain("LogLines", viewModel);
    }

    [Fact]
    public void Danbooru_UsesStablePostIdCursorAndReportsTheFinalSummary()
    {
        string content = ReadRepoFile(Path.Combine("ViewModel", "DatasetDownloaderViewModel.cs"));

        Assert.Contains("BuildDanbooruPostsRequestResource", content);
        Assert.Contains("page=b{beforePostId.Value}", content);
        Assert.Contains("当前页均已处理，继续检查更早的帖子", content);
        Assert.Contains("IsBanned = post.is_banned", content);
        Assert.Contains("IsDeleted = post.is_deleted", content);
        Assert.Contains("DownloadOutcome.Banned", content);
        Assert.Contains("DownloadOutcome.Deleted", content);
        Assert.Contains("DownloadOutcome.Unavailable", content);
        Assert.Contains("已屏蔽 {counters.Banned} 张", content);
        Assert.Contains("无公开原图链接 {counters.Unavailable} 张", content);
        Assert.DoesNotContain("&page={page}", content);
    }

    [Fact]
    public void DatasetDownloader_IsAlwaysVisibleWhileHomeFiveClickPromptRemains()
    {
        string downloaderPage = ReadRepoFile("DanbooruCrawlerView.xaml.cs");
        string catalog = ReadRepoFile(Path.Combine("Framework", "Pipeline", "PipelineCatalog.cs"));
        string mainWindow = ReadRepoFile("MainWindow.xaml.cs");

        Assert.Contains("[ToolPage(\"数据集下载器\", 290)]", downloaderPage);
        Assert.DoesNotContain("[ToolPage(\"数据集下载器\", 290, true)]", downloaderPage);
        Assert.Contains("typeof(DatasetDownloaderPipelineNode), () => new DatasetDownloaderPipelineNode())", catalog);
        Assert.DoesNotContain("DatasetDownloaderPipelineNode(), requiresUnlock: true", catalog);
        Assert.Contains("_secretImageClickCount < 5", mainWindow);
        Assert.Contains("\"再敲咬你啊\"", mainWindow);
    }

    [Fact]
    public void WorkflowParameterGrid_PreservesDatasetDownloaderControlsAndLabels()
    {
        string content = ReadRepoFile(Path.Combine("ViewModel", "PipelineNodeParameterViewModels.cs"));
        string canvas = ReadRepoFile(Path.Combine("ViewModel", "PipelineGraphViewModels.cs"));
        string scheduler = ReadRepoFile("PipelineSchedulerView.xaml");

        Assert.Contains("P(\"InputText\", \"ID / 标签\")", content);
        Assert.Contains("P(\"OutputDirectory\", \"输出目录\"", content);
        Assert.Contains("P(\"IncludeArtist\", \"Artist\")", content);
        Assert.DoesNotContain("P(\"DanbooruToken\", \"Danbooru 令牌\"", content);
        Assert.DoesNotContain("P(\"PixivRefreshToken\", \"Pixiv 令牌\"", content);
        Assert.DoesNotContain("A(\"登录获取\", \"GetPixivRefreshTokenCommand\")", content);
        Assert.DoesNotContain("A(\"Danbooru 正则手动获取\", \"OpenDanbooruManualCollectorCommand\")", content);
        Assert.Contains("CreateWorkflowParameters(PipelineNode)", canvas);
        Assert.Contains("CreateWorkflowActions(PipelineNode)", canvas);
        Assert.Contains("PipelinePasswordParameterEditorTemplate", scheduler);
        Assert.Contains("PipelineActionButtonTemplate", scheduler);
        Assert.DoesNotContain("P(\"InputText\", \"输入内容\")", content);
    }

    [Fact]
    public void WorkflowOnlyParameters_ExcludeInputsRepresentedByNodeConnections()
    {
        string content = ReadRepoFile(Path.Combine("ViewModel", "PipelineNodeParameterViewModels.cs"));
        int workflowDefinitionsStart = content.IndexOf("WorkflowOnlyDefinitions", StringComparison.Ordinal);
        int workflowActionsStart = content.IndexOf("WorkflowActionDefinitions", StringComparison.Ordinal);
        string workflowDefinitions = content[workflowDefinitionsStart..workflowActionsStart];

        Assert.DoesNotContain("InputPath", workflowDefinitions);
        Assert.DoesNotContain("InputDirectory", workflowDefinitions);
        Assert.DoesNotContain("InputFolder", workflowDefinitions);
        Assert.DoesNotContain("FolderPath", workflowDefinitions);
        Assert.DoesNotContain("VideoPath", workflowDefinitions);
        Assert.DoesNotContain("SelectedFolderPath", workflowDefinitions);
        Assert.DoesNotContain("DanbooruToken", workflowDefinitions);
        Assert.DoesNotContain("PixivRefreshToken", workflowDefinitions);
    }

    [Fact]
    public void WorkflowFolderPickerNodes_UseExplicitInputAndOutputRoles()
    {
        string pipelineNodes = ReadRepoFile(Path.Combine("ViewModel", "PipelineNodes.cs"));
        string catalog = ReadRepoFile(Path.Combine("Framework", "Pipeline", "PipelineCatalog.cs"));
        string parameters = ReadRepoFile(Path.Combine("ViewModel", "PipelineNodeParameterViewModels.cs"));
        string graph = ReadRepoFile(Path.Combine("ViewModel", "PipelineGraphViewModels.cs"));

        Assert.Contains("\\u9009\\u62E9\\u8F93\\u5165\\u6587\\u4EF6\\u5939", pipelineNodes);
        Assert.Contains("OutputFolderPickerPipelineNode", pipelineNodes);
        Assert.Contains("IPipelineOutputPathSelectorNode", pipelineNodes);
        Assert.Contains("OutputFolderPickerPipelineNode", catalog);
        Assert.Contains("P(\"FolderPath\", \"输入文件夹\"", parameters);
        Assert.Contains("P(\"OutputFolderPath\", \"输出文件夹\"", parameters);
        Assert.Contains("GetOutputPathSelectorNode", graph);
    }

    [Fact]
    public void WorkflowStorage_PersistsAndRestoresCanvasNodeLocations()
    {
        string storage = ReadRepoFile(Path.Combine("Framework", "Pipeline", "WorkflowStorage.cs"));
        string scheduler = ReadRepoFile(Path.Combine("ViewModel", "PipelineSchedulerViewModel.cs"));

        Assert.Contains("WorkflowNodeLayout", storage);
        Assert.Contains("LocationX", storage);
        Assert.Contains("LocationY", storage);
        Assert.Contains("LoadWithLayout", storage);
        Assert.Contains("new WorkflowNodeLayout", scheduler);
        Assert.Contains("LoadWithLayout(name, _downloadersUnlocked)", scheduler);
        Assert.Contains("GetSavedLocation", scheduler);
    }

    [Fact]
    public void PipelineScheduler_UsesBezierConnections()
    {
        string content = ReadRepoFile("PipelineSchedulerView.xaml");

        Assert.Contains("<nodify:Connection Source=\"{Binding Output.Anchor}\"", content);
        Assert.DoesNotContain("<nodify:CircuitConnection", content);
    }

    [Fact]
    public void PipelineScheduler_HighlightsTheNodeCurrentlyExecuting()
    {
        string graph = ReadRepoFile(Path.Combine("ViewModel", "PipelineGraphViewModels.cs"));
        string scheduler = ReadRepoFile(Path.Combine("ViewModel", "PipelineSchedulerViewModel.cs"));
        string view = ReadRepoFile("PipelineSchedulerView.xaml");
        string runner = ReadRepoFile(Path.Combine("Framework", "Pipeline", "PipelineRunner.cs"));

        Assert.Contains("bool IsExecuting", graph);
        Assert.Contains("SetExecutingCanvasNode", scheduler);
        Assert.Contains("SetExecutingCanvasNode(null)", scheduler);
        Assert.Contains("nodeStarted?.Invoke(node)", runner);
        Assert.Contains("Binding=\"{Binding IsExecuting}\"", view);
        Assert.Contains("<DropShadowEffect Color=\"#FF2497FF\"", view);
    }

    [Fact]
    public void SameSourceGrouping_ExposesNamedOutputPortsForItsActualFolders()
    {
        string viewModel = ReadRepoFile(Path.Combine("ViewModel", "SameSourceImageGroupingViewModel.cs"));
        string graph = ReadRepoFile(Path.Combine("ViewModel", "PipelineGraphViewModels.cs"));
        string scheduler = ReadRepoFile(Path.Combine("ViewModel", "PipelineSchedulerViewModel.cs"));
        string storage = ReadRepoFile(Path.Combine("Framework", "Pipeline", "WorkflowStorage.cs"));

        Assert.Contains("IPipelineOutputPortProvider", viewModel);
        Assert.Contains("new(\"root\", \"输出\")", viewModel);
        Assert.Contains("new(\"original\", \"原图\", \"原图\")", viewModel);
        Assert.Contains("new(\"thumbnail\", \"缩略图\", \"缩略图\")", viewModel);
        Assert.Contains("new(\"duplicate\", \"副本\", \"副本\")", viewModel);
        Assert.Contains("new(\"contained\", \"被包含\", \"被包含\")", viewModel);
        Assert.Contains("new(\"difference\", \"差分\", \"差分\")", viewModel);
        Assert.Contains("new(\"other\", \"其他\", \"其他\")", viewModel);
        Assert.Contains("GetOutputPorts(PipelineNode)", graph);
        Assert.Contains("WorkflowOutputPortKey", scheduler);
        Assert.Contains("OutputPortKey", storage);
        Assert.Contains("只能选择一个输出端口", scheduler);
    }

    private static string ReadRepoFile(string relativePath)
    {
        string repositoryRoot = FindRepositoryRoot();
        string fullPath = Path.Combine(repositoryRoot, relativePath);
        return File.ReadAllText(fullPath);
    }

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? current = new(AppContext.BaseDirectory);
        while (current is not null)
        {
            string solutionPath = Path.Combine(current.FullName, "CyanTools.sln");
            if (File.Exists(solutionPath))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate repository root.");
    }
}

public sealed class InputStateStoreTests : IDisposable
{
    private readonly string _stateFilePath = Path.Combine(
        Path.GetTempPath(),
        "CyanToolsTests",
        Guid.NewGuid().ToString("N"),
        "input-state.json");

    [Fact]
    public void SaveAndRestore_RetainsOrdinaryInputsButNeverSensitiveOrRuntimeValues()
    {
        var store = new InputStateStore(_stateFilePath);
        store.Save(new SampleInputs
        {
            SearchText = "cat_ears",
            OutputDirectory = @"D:\datasets\cat_ears",
            DownloadMode = SampleDownloadMode.Tag,
            EnableSource = true,
            RetryCount = 3,
            ApiToken = "do-not-store",
            StatusMessage = "completed",
            IsRunning = true,
            LayoutFlag = false
        });

        var restored = new SampleInputs
        {
            ApiToken = "unchanged-secret",
            StatusMessage = "waiting",
            IsRunning = true,
            LayoutFlag = true
        };

        new InputStateStore(_stateFilePath).Restore(restored);

        Assert.Equal("cat_ears", restored.SearchText);
        Assert.Equal(@"D:\datasets\cat_ears", restored.OutputDirectory);
        Assert.Equal(SampleDownloadMode.Tag, restored.DownloadMode);
        Assert.True(restored.EnableSource);
        Assert.Equal(3, restored.RetryCount);
        Assert.Equal("unchanged-secret", restored.ApiToken);
        Assert.Equal("waiting", restored.StatusMessage);
        Assert.True(restored.IsRunning);
        Assert.True(restored.LayoutFlag);
        Assert.DoesNotContain("do-not-store", File.ReadAllText(_stateFilePath), StringComparison.Ordinal);
        Assert.DoesNotContain(nameof(SampleInputs.LayoutFlag), File.ReadAllText(_stateFilePath), StringComparison.Ordinal);
    }

    [Fact]
    public void Restore_IgnoresStaleValuesForPropertiesWithoutPublicSetters()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_stateFilePath)!);
        string stateKey = typeof(SampleInputs).FullName!;
        File.WriteAllText(
            _stateFilePath,
            $$"""
            {
              "{{stateKey}}": {
                "LayoutFlag": false
              }
            }
            """);
        var restored = new SampleInputs
        {
            LayoutFlag = true
        };

        new InputStateStore(_stateFilePath).Restore(restored);

        Assert.True(restored.LayoutFlag);
    }

    public void Dispose()
    {
        string? directory = Path.GetDirectoryName(_stateFilePath);
        if (!string.IsNullOrWhiteSpace(directory) && Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private enum SampleDownloadMode
    {
        Artist,
        Tag
    }

    private sealed class SampleInputs
    {
        public string SearchText { get; set; } = string.Empty;

        public string OutputDirectory { get; set; } = string.Empty;

        public SampleDownloadMode DownloadMode { get; set; }

        public bool EnableSource { get; set; }

        public int RetryCount { get; set; }

        public string ApiToken { get; set; } = string.Empty;

        public string StatusMessage { get; set; } = string.Empty;

        public bool IsRunning { get; set; }

        public bool LayoutFlag { get; internal set; }
    }
}

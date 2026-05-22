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
            "<TextBox[^>]*SelectedFolderPath[^>]*/>",
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
            "<TextBox[^>]*Text=\"\\{Binding LogText[^\\\"]*\\}\"[^>]*/>",
            RegexOptions.Singleline);

        Assert.True(logTextBox.Success);
        Assert.Contains("Mode=OneWay", logTextBox.Value);
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

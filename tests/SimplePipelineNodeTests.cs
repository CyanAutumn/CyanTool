using System.ComponentModel;
using CyanTools.Pipeline;
using Xunit;

namespace CyanTools.PathDefaults.Tests;

public sealed class SimplePipelineNodeTests
{
    [Fact]
    public void InnerPropertyChanges_AreForwardedToNodePropertyChanged()
    {
        var node = new TestSimplePipelineNode();
        string? raisedPropertyName = null;
        node.PropertyChanged += (_, args) => raisedPropertyName = args.PropertyName;

        node.Inner.InputPath = "updated";

        Assert.Equal(nameof(TestInnerViewModel.InputPath), raisedPropertyName);
    }

    private sealed class TestSimplePipelineNode : SimplePipelineNode<TestInnerViewModel>
    {
        public TestSimplePipelineNode() : base("测试节点")
        {
        }

        public override PipelineNodeViewModelBase Clone() => throw new NotSupportedException();
    }

    public sealed class TestInnerViewModel : INotifyPropertyChanged
    {
        private string _inputPath = string.Empty;

        public string InputPath
        {
            get => _inputPath;
            set
            {
                if (_inputPath == value)
                {
                    return;
                }

                _inputPath = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(InputPath)));
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
    }
}

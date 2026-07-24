using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;
using CyanTools.Pipeline;

namespace CyanTools.ViewModel
{
    public sealed class PipelineCanvasNodeViewModel : INotifyPropertyChanged
    {
        private Point _location;
        private bool _isSelected;
        private bool _isExpanded = true;
        private bool _isExecuting;

        public PipelineCanvasNodeViewModel(PipelineNodeViewModelBase pipelineNode, Point location)
        {
            PipelineNode = pipelineNode ?? throw new ArgumentNullException(nameof(pipelineNode));
            Location = location;
            EditableParameters = PipelineNodeParameterFactory.CreateWorkflowParameters(PipelineNode);
            EditableActions = PipelineNodeParameterFactory.CreateWorkflowActions(PipelineNode);

            var directoryNode = PipelineNode.GetDirectoryAutomationNode();
            var runtimeOutputNode = PipelineNode.GetRuntimeOutputNode();
            var outputPathSelectorNode = PipelineNode.GetOutputPathSelectorNode();

            if ((directoryNode != null && directoryNode.DirectoryBehavior != PipelineDirectoryBehavior.None) ||
                outputPathSelectorNode != null)
            {
                Input.Add(new PipelineCanvasConnectorViewModel(this, "输入", isInput: true));
            }

            if ((directoryNode != null && directoryNode.DirectoryBehavior != PipelineDirectoryBehavior.None) ||
                runtimeOutputNode != null)
            {
                foreach (PipelineOutputPort port in GetOutputPorts(PipelineNode))
                {
                    Output.Add(new PipelineCanvasConnectorViewModel(
                        this,
                        port.Title,
                        isInput: false,
                        port.Key,
                        port.RelativePath));
                }
            }

            PipelineNode.PropertyChanged += OnPipelineNodePropertyChanged;
        }

        public PipelineNodeViewModelBase PipelineNode { get; }
        public ObservableCollection<PipelineNodeParameterViewModel> EditableParameters { get; }
        public ObservableCollection<PipelineNodeActionViewModel> EditableActions { get; }
        public ObservableCollection<PipelineCanvasConnectorViewModel> Input { get; } = new();
        public ObservableCollection<PipelineCanvasConnectorViewModel> Output { get; } = new();
        public string DisplayName => PipelineNode.DisplayName;
        public bool HasEditableParameters => EditableParameters.Count > 0;
        public bool HasEditableActions => EditableActions.Count > 0;
        public bool HasInput => Input.Count > 0;
        public bool HasOutput => Output.Count > 0;

        public Point Location
        {
            get => _location;
            set => SetField(ref _location, value);
        }

        public bool IsSelected
        {
            get => _isSelected;
            set => SetField(ref _isSelected, value);
        }

        public bool IsExpanded
        {
            get => _isExpanded;
            set => SetField(ref _isExpanded, value);
        }




        public bool IsExecuting
        {
            get => _isExecuting;
            set => SetField(ref _isExecuting, value);
        }

        public string DirectoryBehaviorText
        {
            get
            {
                if (PipelineNode.GetOutputPathSelectorNode() != null)
                {
                    return "重定向上一个节点输出";
                }

                var directoryNode = PipelineNode.GetDirectoryAutomationNode();
                if (directoryNode == null || directoryNode.DirectoryBehavior == PipelineDirectoryBehavior.None)
                {
                    return PipelineNode.GetRuntimeOutputNode() != null ? "运行时输出文件夹" : "无目录端口";
                }

                return directoryNode.DirectoryBehavior switch
                {
                    PipelineDirectoryBehavior.GeneratedOutput => "输出文件夹自动生成",
                    PipelineDirectoryBehavior.InPlace => "输出等于输入文件夹",
                    _ => "自定义目录"
                };
            }
        }

        public string InputPathPreview
        {
            get
            {
                var directoryNode = PipelineNode.GetDirectoryAutomationNode();
                return string.IsNullOrWhiteSpace(directoryNode?.PipelineInputPath)
                    ? "未设置"
                    : directoryNode.PipelineInputPath;
            }
        }

        public string OutputPathPreview
        {
            get
            {
                var directoryNode = PipelineNode.GetDirectoryAutomationNode();
                if (directoryNode != null && !string.IsNullOrWhiteSpace(directoryNode.PipelineOutputPath))
                {
                    return directoryNode.PipelineOutputPath;
                }

                var runtimeOutputNode = PipelineNode.GetRuntimeOutputNode();
                if (runtimeOutputNode != null &&
                    runtimeOutputNode.TryGetRuntimeOutputPath(out string runtimeOutputPath) &&
                    !string.IsNullOrWhiteSpace(runtimeOutputPath))
                {
                    return runtimeOutputPath;
                }

                return PipelineNode.GetRuntimeOutputNode() != null ? "运行后确定" : "无输出";
            }
        }

        public void Dispose()
        {
            PipelineNode.PropertyChanged -= OnPipelineNodePropertyChanged;
        }

        public void RefreshPathPreview()
        {
            OnPropertyChanged(nameof(InputPathPreview));
            OnPropertyChanged(nameof(OutputPathPreview));
            OnPropertyChanged(nameof(DirectoryBehaviorText));
        }

        private void OnPipelineNodePropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            foreach (PipelineNodeParameterViewModel parameter in EditableParameters)
            {
                parameter.Refresh();
            }

            RefreshPathPreview();
        }

        private static IReadOnlyList<PipelineOutputPort> GetOutputPorts(PipelineNodeViewModelBase node)
        {
            IReadOnlyList<PipelineOutputPort>? configuredPorts = node.GetOutputPortProvider()?.GetPipelineOutputPorts();
            if (configuredPorts == null || configuredPorts.Count == 0)
            {
                return [new PipelineOutputPort("root", "输出")];
            }

            var ports = configuredPorts
                .Where(port => !string.IsNullOrWhiteSpace(port.Key) && !string.IsNullOrWhiteSpace(port.Title))
                .GroupBy(port => port.Key, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .ToList();

            if (!ports.Any(port => port.IsRootOutput))
            {
                ports.Insert(0, new PipelineOutputPort("root", "输出"));
            }

            return ports;
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
        {
            if (Equals(field, value))
            {
                return false;
            }

            field = value;
            OnPropertyChanged(propertyName);
            return true;
        }
    }

    public sealed class PipelineCanvasConnectorViewModel : INotifyPropertyChanged
    {
        private Point _anchor;
        private bool _isConnected;

        public PipelineCanvasConnectorViewModel(
            PipelineCanvasNodeViewModel node,
            string title,
            bool isInput,
            string? portKey = null,
            string? relativePath = null)
        {
            Node = node ?? throw new ArgumentNullException(nameof(node));
            Title = title ?? throw new ArgumentNullException(nameof(title));
            IsInput = isInput;
            PortKey = portKey;
            RelativePath = relativePath;
        }

        public PipelineCanvasNodeViewModel Node { get; }
        public string Title { get; }
        public bool IsInput { get; }
        public string? PortKey { get; }
        public string? RelativePath { get; }
        public bool IsRootOutput => !IsInput && string.IsNullOrWhiteSpace(RelativePath);

        public Point Anchor
        {
            get => _anchor;
            set => SetField(ref _anchor, value);
        }

        public bool IsConnected
        {
            get => _isConnected;
            set => SetField(ref _isConnected, value);
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
        {
            if (Equals(field, value))
            {
                return false;
            }

            field = value;
            OnPropertyChanged(propertyName);
            return true;
        }
    }

    public sealed class PipelineCanvasConnectionViewModel
    {
        public PipelineCanvasConnectorViewModel Input { get; init; } = null!;
        public PipelineCanvasConnectorViewModel Output { get; init; } = null!;
    }

    public sealed class PipelineCanvasPendingConnectionViewModel : INotifyPropertyChanged
    {
        private PipelineCanvasConnectorViewModel? _source;
        private PipelineCanvasConnectorViewModel? _target;
        private bool _isVisible;
        private Point _targetLocation;

        public PipelineCanvasConnectorViewModel? Source
        {
            get => _source;
            set => SetField(ref _source, value);
        }

        public PipelineCanvasConnectorViewModel? Target
        {
            get => _target;
            set => SetField(ref _target, value);
        }

        public bool IsVisible
        {
            get => _isVisible;
            set => SetField(ref _isVisible, value);
        }

        public Point TargetLocation
        {
            get => _targetLocation;
            set => SetField(ref _targetLocation, value);
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
        {
            if (Equals(field, value))
            {
                return false;
            }

            field = value;
            OnPropertyChanged(propertyName);
            return true;
        }
    }
}

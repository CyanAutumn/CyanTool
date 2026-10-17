using System.Windows.Controls;
using CyanTools.Navigation;
using CyanTools.ViewModel;

namespace CyanTools;

[ToolPage("远景图检测", 248)]
public partial class FarSceneDetectionView : UserControl
{
    public FarSceneDetectionView()
    {
        InitializeComponent();
        var model = new FarSceneDetectionViewModel();
        model.OperationFailed += message => MessageBox.Show(message, "生成索引失败");
        DataContext = model;
    }
}

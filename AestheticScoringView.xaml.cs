using System.Windows.Controls;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using CyanTools.Navigation;
using CyanTools.ViewModel;

namespace CyanTools;

[ToolPage("美学评分", 247)]
public partial class AestheticScoringView : UserControl
{
    public AestheticScoringView()
    {
        InitializeComponent();
        DataContext = new AestheticScoringViewModel();
        Loaded += (_, _) => LocalizationManager.LanguageChanged += LanguageChanged;
        Unloaded += (_, _) => LocalizationManager.LanguageChanged -= LanguageChanged;
    }

    private void LanguageChanged(object? sender, EventArgs e)
    {

        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() => RefreshLayout(this)));
    }

    private static void RefreshLayout(DependencyObject element)
    {
        for (int index = 0; index < VisualTreeHelper.GetChildrenCount(element); index++)
            RefreshLayout(VisualTreeHelper.GetChild(element, index));
        if (element is UIElement control)
        {
            control.InvalidateMeasure();
            control.InvalidateArrange();
        }
    }
}

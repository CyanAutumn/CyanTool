using System.Windows;
using System.Windows.Controls;
using CyanTools.ViewModel;

namespace CyanTools.Utils
{
    public sealed class TagItemTemplateSelector : DataTemplateSelector
    {
        public DataTemplate? TagTemplate { get; set; }
        public DataTemplate? InputTemplate { get; set; }

        public override DataTemplate SelectTemplate(object item, DependencyObject container)
        {
            if (item is TagInputPlaceholder)
            {
                return InputTemplate ?? base.SelectTemplate(item, container);
            }

            if (item is TagTokenViewModel)
            {
                return TagTemplate ?? base.SelectTemplate(item, container);
            }

            return base.SelectTemplate(item, container);
        }
    }
}

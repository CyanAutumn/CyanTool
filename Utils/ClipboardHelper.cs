
using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;

namespace CyanTools.Utils
{
    public static class ClipboardHelper
    {
        public static void SetTextWithRetry(string text)
        {
            for (int i = 0; i < 10; i++)
            {
                try
                {
                    var data = new DataObject();
                    data.SetData(DataFormats.Text, text);
                    Clipboard.SetDataObject(data, true);
                    return;
                }
                catch (COMException)
                {
                    Thread.Sleep(100);
                }
            }
            MessageBox.Show("无法访问剪贴板。请稍后重试。", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}

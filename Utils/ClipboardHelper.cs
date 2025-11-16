
using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

namespace CyanTools.Utils
{
    public static class ClipboardHelper
    {
        public static Task<bool> TrySetTextAsync(string text, int maxAttempts = 5, int delayMilliseconds = 100)
        {
            var tcs = new TaskCompletionSource<bool>();

            var thread = new Thread(() =>
            {
                try
                {
                    bool success = TrySetTextWithRetryInternal(text, maxAttempts, delayMilliseconds);
                    tcs.SetResult(success);
                }
                catch (Exception ex)
                {
                    tcs.SetException(ex);
                }
            })
            {
                IsBackground = true
            };

            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            return tcs.Task;
        }

        private static bool TrySetTextWithRetryInternal(string text, int maxAttempts, int delayMilliseconds)
        {
            for (int i = 0; i < maxAttempts; i++)
            {
                try
                {
                    var data = new DataObject();
                    data.SetData(DataFormats.Text, text);
                    Clipboard.SetDataObject(data, true);
                    return true;
                }
                catch (COMException)
                {
                    Thread.Sleep(delayMilliseconds);
                }
            }

            return false;
        }
    }
}

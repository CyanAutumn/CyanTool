using System;
using System.Windows;

namespace CyanTools.Utils;


public static class UiDispatch
{
    private static readonly object HeadlessGate = new();
    public static bool CheckAccess() => Application.Current?.Dispatcher.CheckAccess() ?? true;

    public static void Invoke(Action action)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher != null && !dispatcher.CheckAccess()) dispatcher.Invoke(action);
        else if (dispatcher != null) action();
        else { lock (HeadlessGate) action(); }
    }
}


public sealed class ExecutionProgress<T> : IProgress<T>
{
    private readonly Action<T> _report;
    public ExecutionProgress(Action<T> report) => _report = report ?? throw new ArgumentNullException(nameof(report));
    public void Report(T value) => UiDispatch.Invoke(() => _report(value));
}

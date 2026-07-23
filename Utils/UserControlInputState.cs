using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;

namespace CyanTools.Utils;





public static class UserControlInputState
{
    private static readonly object SyncRoot = new();
    private static readonly HashSet<UserControl> RegisteredControls = new(ReferenceEqualityComparer.Instance);
    private static readonly ConditionalWeakTable<UserControl, RestoreTracker> RestoreTrackers = new();
    private static bool _isInitialized;

    public static void Initialize()
    {
        lock (SyncRoot)
        {
            if (_isInitialized)
            {
                return;
            }

            EventManager.RegisterClassHandler(
                typeof(UserControl),
                FrameworkElement.LoadedEvent,
                new RoutedEventHandler(OnUserControlLoaded),
                handledEventsToo: true);
            EventManager.RegisterClassHandler(
                typeof(UserControl),
                FrameworkElement.UnloadedEvent,
                new RoutedEventHandler(OnUserControlUnloaded),
                handledEventsToo: true);
            _isInitialized = true;
        }
    }

    public static void SaveAll()
    {
        List<object> dataContexts;
        lock (SyncRoot)
        {
            dataContexts = RegisteredControls
                .Select(control => control.DataContext)
                .Where(dataContext => dataContext != null)
                .Cast<object>()
                .Distinct(ReferenceEqualityComparer.Instance)
                .ToList();
        }

        foreach (object dataContext in dataContexts)
        {
            InputStateStore.Default.Save(dataContext);
        }
    }

    private static void OnUserControlLoaded(object sender, RoutedEventArgs e)
    {
        if (e.OriginalSource is not UserControl control)
        {
            return;
        }

        Register(control);
        RestoreOnce(control, control.DataContext);
    }

    private static void OnUserControlUnloaded(object sender, RoutedEventArgs e)
    {
        if (e.OriginalSource is UserControl control && control.DataContext != null)
        {
            InputStateStore.Default.Save(control.DataContext);
        }
    }

    private static void Register(UserControl control)
    {
        lock (SyncRoot)
        {
            if (!RegisteredControls.Add(control))
            {
                return;
            }

            control.DataContextChanged += OnDataContextChanged;
        }
    }

    private static void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (sender is not UserControl control)
        {
            return;
        }

        if (e.OldValue != null)
        {
            InputStateStore.Default.Save(e.OldValue);
        }

        RestoreOnce(control, e.NewValue);
    }

    private static void RestoreOnce(UserControl control, object? dataContext)
    {
        if (dataContext == null)
        {
            return;
        }

        RestoreTracker tracker = RestoreTrackers.GetOrCreateValue(control);
        if (!tracker.RestoredDataContexts.Add(dataContext))
        {
            return;
        }

        InputStateStore.Default.Restore(dataContext);
    }

    private sealed class RestoreTracker
    {
        public HashSet<object> RestoredDataContexts { get; } = new(ReferenceEqualityComparer.Instance);
    }
}

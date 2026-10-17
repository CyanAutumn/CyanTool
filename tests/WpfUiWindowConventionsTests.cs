using Xunit;
using System.Text.RegularExpressions;

namespace CyanTools.Tests;

public sealed class WpfUiWindowConventionsTests
{
    private static readonly string[] MigratedControlNames =
    {
        "Button",
        "TextBox",
        "ListView",
        "DataGrid"
    };

    private static readonly string[] FluentWindowNames =
    {
        "AddTaggerWindow",
        "ClusterDistributionWindow",
        "DanbooruManualCollectorWindow",
        "PixivAuthWindow",
        "UpdateWindow"
    };

    [Fact]
    public void OrdinaryAuxiliaryWindows_UseFluentWindowWithoutBackdrop()
    {
        foreach (string windowName in FluentWindowNames)
        {
            string xaml = ReadRepoFile($"{windowName}.xaml").Trim();
            string codeBehind = ReadRepoFile($"{windowName}.xaml.cs");

            Assert.StartsWith("<ui:FluentWindow", xaml, StringComparison.Ordinal);
            Assert.EndsWith("</ui:FluentWindow>", xaml, StringComparison.Ordinal);
            Assert.Contains("ExtendsContentIntoTitleBar=\"False\"", xaml, StringComparison.Ordinal);
            Assert.Contains("WindowBackdropType=\"None\"", xaml, StringComparison.Ordinal);
            Assert.Contains($"partial class {windowName} : FluentWindow", codeBehind, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void TransparentMonitorWidget_RemainsANativeWindow()
    {
        string xaml = ReadRepoFile("SystemMonitorWidgetWindow.xaml").Trim();
        string codeBehind = ReadRepoFile("SystemMonitorWidgetWindow.xaml.cs");

        Assert.StartsWith("<Window", xaml, StringComparison.Ordinal);
        Assert.EndsWith("</Window>", xaml, StringComparison.Ordinal);
        Assert.Contains("AllowsTransparency=\"True\"", xaml, StringComparison.Ordinal);
        Assert.Contains("WindowStyle=\"None\"", xaml, StringComparison.Ordinal);
        Assert.Contains("partial class SystemMonitorWidgetWindow : Window", codeBehind, StringComparison.Ordinal);
    }

    [Fact]
    public void ActiveViews_UseWpfUiImplementationsForSupportedInteractiveControls()
    {
        string root = FindRepositoryRoot();
        string[] activeXamlFiles = Directory
            .EnumerateFiles(root, "*.xaml", SearchOption.TopDirectoryOnly)
            .Where(path => !Path.GetFileName(path).Contains("DESKTOP", StringComparison.OrdinalIgnoreCase))
            .Where(path => !string.Equals(Path.GetFileName(path), "SystemMonitorWidgetWindow.xaml", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        foreach (string path in activeXamlFiles)
        {
            string xaml = File.ReadAllText(path);
            foreach (string controlName in MigratedControlNames)
            {
                if (controlName == "TextBox")
                {
                    var document = System.Xml.Linq.XDocument.Parse(xaml);
                    Assert.DoesNotContain(document.Descendants(), element =>
                        element.Name.LocalName == "TextBox" &&
                        element.Name.NamespaceName == "http://schemas.microsoft.com/winfx/2006/xaml/presentation" &&
                        !string.Equals((string?)element.Attribute("IsReadOnly"), "True", StringComparison.OrdinalIgnoreCase));
                    continue;
                }
                Assert.DoesNotMatch(
                    new Regex($"<{Regex.Escape(controlName)}(?=[\\s>.])", RegexOptions.CultureInvariant),
                    xaml);
            }
        }

        string styles = ReadRepoFile(Path.Combine("Themes", "CyanModernStyles.xaml"));
        Assert.Contains("TargetType=\"{x:Type ui:Button}\"", styles, StringComparison.Ordinal);
        Assert.Contains("TargetType=\"{x:Type ui:TextBox}\"", styles, StringComparison.Ordinal);
        Assert.Contains("TargetType=\"{x:Type ui:DataGrid}\"", styles, StringComparison.Ordinal);
    }

    [Fact]
    public void LocalizedMessageBoxes_UseWpfUiWithACompatibilityFallback()
    {
        string source = ReadRepoFile(Path.Combine("Localization", "LocalizedMessageBox.cs"));

        Assert.Contains("new WpfMessageBox", source, StringComparison.Ordinal);
        Assert.Contains("ShowDialogAsync().GetAwaiter().GetResult()", source, StringComparison.Ordinal);
        Assert.Contains("ConfigureButtons", source, StringComparison.Ordinal);
        Assert.Contains("WpfMessageBoxResult.Primary", source, StringComparison.Ordinal);
        Assert.Contains("WpfMessageBoxResult.Secondary", source, StringComparison.Ordinal);
        Assert.Contains("ShowNative", source, StringComparison.Ordinal);
        Assert.Contains("MessageBoxOptions.None", source, StringComparison.Ordinal);
    }

    private static string ReadRepoFile(string relativePath)
    {
        return File.ReadAllText(Path.Combine(FindRepositoryRoot(), relativePath));
    }

    private static string FindRepositoryRoot()
    {
        string? current = AppContext.BaseDirectory;
        while (!string.IsNullOrWhiteSpace(current))
        {
            if (File.Exists(Path.Combine(current, "CyanTools.csproj")))
            {
                return current;
            }

            current = Directory.GetParent(current)?.FullName;
        }

        throw new DirectoryNotFoundException("Could not locate the CyanTools repository root.");
    }
}

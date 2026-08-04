using CyanTools.Localization;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using Xunit;

[assembly: CollectionBehavior(DisableTestParallelization = true)]

public sealed class LocalizationTests
{
    private static readonly Regex CjkRegex = new(@"\p{IsCJKUnifiedIdeographs}", RegexOptions.CultureInvariant);

    [Fact]
    public void CoreStaticAndDynamicTextSwitchesAcrossAllThreeLanguages()
    {
        try
        {
            LocalizationManager.SetLanguage(AppLanguage.English);
            Assert.Equal("Start Download", LocalizationManager.Translate("开始下载"));
            Assert.Equal(
                "Danbooru: Waiting to download; added 3, skipped 2",
                LocalizationManager.Translate("Danbooru：等待下载，本次新增 3 张，跳过 2 张"));

            LocalizationManager.SetLanguage(AppLanguage.Japanese);
            Assert.Equal("ダウンロード開始", LocalizationManager.Translate("开始下载"));
            Assert.Equal(
                "Danbooru：ダウンロード待機中、今回追加 3 枚、スキップ 2 枚",
                LocalizationManager.Translate("Danbooru：等待下载，本次新增 3 张，跳过 2 张"));

            LocalizationManager.SetLanguage(AppLanguage.Chinese);
            Assert.Equal("开始下载", LocalizationManager.Translate("开始下载"));
        }
        finally
        {
            LocalizationManager.SetLanguage(AppLanguage.Chinese);
        }
    }

    [Fact]
    public void ConverterFormatsTranslatedRuntimeValues()
    {
        var converter = new LocalizedTextConverter();
        try
        {
            LocalizationManager.SetLanguage(AppLanguage.English);
            Assert.Equal(
                "Running: Start Download",
                converter.Convert("开始下载", typeof(string), "运行中: {0}", CultureInfo.InvariantCulture));

            LocalizationManager.SetLanguage(AppLanguage.Japanese);
            Assert.Equal(
                "実行中：ダウンロード開始",
                converter.Convert("开始下载", typeof(string), "运行中: {0}", CultureInfo.InvariantCulture));
        }
        finally
        {
            LocalizationManager.SetLanguage(AppLanguage.Chinese);
        }
    }

    [Fact]
    public void ApplyToRefreshesOptedInBindingsButPreservesUserContent()
    {
        RunSta(() =>
        {
            var status = new ObservableText("等待训练数据");
            var localized = new TextBlock();
            BindingOperations.SetBinding(
                localized,
                TextBlock.TextProperty,
                new Binding(nameof(ObservableText.Value))
                {
                    Source = status,
                    Converter = new LocalizedTextConverter()
                });

            var userContent = new TextBlock();
            BindingOperations.SetBinding(
                userContent,
                TextBlock.TextProperty,
                new Binding(nameof(ObservableText.Value))
                {
                    Source = new ObservableText("开始下载")
                });

            try
            {
                LocalizationManager.SetLanguage(AppLanguage.English);
                LocalizationManager.ApplyTo(localized);
                LocalizationManager.ApplyTo(userContent);
                Assert.Equal("Waiting for training data", localized.Text);
                Assert.Equal("开始下载", userContent.Text);

                status.Value = "开始下载";
                Assert.Equal("Start Download", localized.Text);

                LocalizationManager.SetLanguage(AppLanguage.Japanese);
                LocalizationManager.ApplyTo(localized);
                LocalizationManager.ApplyTo(userContent);
                Assert.Equal("ダウンロード開始", localized.Text);
                Assert.Equal("开始下载", userContent.Text);
            }
            finally
            {
                LocalizationManager.SetLanguage(AppLanguage.Chinese);
            }
        });
    }

    [Fact]
    public void ActiveXamlStaticTextHasEnglishAndJapaneseTranslations()
    {
        string root = FindRepositoryRoot();
        var attributeRegex = new Regex(
            "(?:Text|Content|Header|ToolTip|Title|PlaceholderText)\\s*=\\s*\"(?!\\{)([^\"]*\\p{IsCJKUnifiedIdeographs}[^\"]*)\"",
            RegexOptions.CultureInvariant);
        var literals = Directory.EnumerateFiles(root, "*.xaml", SearchOption.AllDirectories)
            .Where(IsActiveSourceFile)
            .SelectMany(path => attributeRegex.Matches(File.ReadAllText(path)).Select(match => match.Groups[1].Value))
            .Select(System.Net.WebUtility.HtmlDecode)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        Assert.NotEmpty(literals);
        AssertTranslationsExist(literals);
    }

    [Fact]
    public void MessageBoxLiteralsHaveEnglishAndJapaneseTranslations()
    {
        string root = FindRepositoryRoot();
        var literals = Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories)
            .Where(IsActiveSourceFile)
            .SelectMany(path => ExtractMessageBoxLiterals(File.ReadAllText(path)))
            .Where(text => CjkRegex.IsMatch(text))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        Assert.NotEmpty(literals);
        AssertTranslationsExist(literals);
    }

    [Fact]
    public void ActiveXamlDoesNotUseChineseStringFormatOrPresetDisplayMemberPath()
    {
        string root = FindRepositoryRoot();
        string combined = string.Join(
            "\n",
            Directory.EnumerateFiles(root, "*.xaml", SearchOption.AllDirectories)
                .Where(IsActiveSourceFile)
                .Select(File.ReadAllText));

        Assert.DoesNotMatch(@"StringFormat=[^}\r\n]*\p{IsCJKUnifiedIdeographs}", combined);
        Assert.DoesNotContain("TaggerSeries.DisplayName", combined, StringComparison.Ordinal);
        Assert.DoesNotContain("ToriiGateCaptionFormats.DisplayName", combined, StringComparison.Ordinal);
    }

    [Fact]
    public void MessageBoxesUseTheLocalizedWrapper()
    {
        string root = FindRepositoryRoot();
        string[] offenders = Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories)
            .Where(IsActiveSourceFile)
            .Where(path => !path.EndsWith("LocalizedMessageBox.cs", StringComparison.OrdinalIgnoreCase))
            .Where(path => File.ReadAllText(path).Contains("System.Windows.MessageBox.Show", StringComparison.Ordinal))
            .ToArray();

        Assert.Empty(offenders);
    }

    private static void AssertTranslationsExist(IEnumerable<string> sourceTexts)
    {
        string[] sources = sourceTexts.ToArray();
        string[] missing = sources.Where(source => !LocalizationManager.HasTranslation(source)).ToArray();
        Assert.True(missing.Length == 0, "Missing translations:\n" + string.Join("\n", missing));
    }

    private static IEnumerable<string> ExtractMessageBoxLiterals(string source)
    {
        int offset = 0;
        while ((offset = source.IndexOf("MessageBox.Show", offset, StringComparison.Ordinal)) >= 0)
        {
            int open = source.IndexOf('(', offset);
            if (open < 0)
            {
                yield break;
            }

            int close = FindClosingParenthesis(source, open);
            if (close < 0)
            {
                yield break;
            }

            string call = source[(open + 1)..close];
            foreach (Match match in Regex.Matches(
                         call,
                         "(?<prefix>\\$?@?)\"(?<value>(?:\\\\.|\"\"|[^\"\\\\])*)\"",
                         RegexOptions.CultureInvariant))
            {
                string value = match.Groups["value"].Value;
                bool verbatim = match.Groups["prefix"].Value.Contains('@');
                value = verbatim ? value.Replace("\"\"", "\"") : Regex.Unescape(value);
                if (match.Groups["prefix"].Value.Contains('$'))
                {
                    int placeholder = 0;
                    value = Regex.Replace(value, @"\{[^{}]+\}", _ => $"{{{placeholder++}}}");
                }

                yield return value;
            }

            offset = close + 1;
        }
    }

    private static int FindClosingParenthesis(string source, int open)
    {
        int depth = 0;
        bool inString = false;
        bool verbatim = false;
        bool escape = false;
        for (int index = open; index < source.Length; index++)
        {
            char character = source[index];
            if (inString)
            {
                if (verbatim && character == '"')
                {
                    if (index + 1 < source.Length && source[index + 1] == '"')
                    {
                        index++;
                    }
                    else
                    {
                        inString = false;
                    }
                }
                else if (!verbatim && escape)
                {
                    escape = false;
                }
                else if (!verbatim && character == '\\')
                {
                    escape = true;
                }
                else if (!verbatim && character == '"')
                {
                    inString = false;
                }

                continue;
            }

            if (character == '"')
            {
                inString = true;
                verbatim = index > 0 && source[index - 1] == '@';
            }
            else if (character == '(')
            {
                depth++;
            }
            else if (character == ')' && --depth == 0)
            {
                return index;
            }
        }

        return -1;
    }

    private static bool IsActiveSourceFile(string path)
        => !path.Contains("-DESKTOP-", StringComparison.OrdinalIgnoreCase)
           && !path.Contains("_DESKTOP_", StringComparison.OrdinalIgnoreCase)
           && !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)
           && !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)
           && !path.Contains($"{Path.DirectorySeparatorChar}tests{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase);

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? current = new(AppContext.BaseDirectory);
        while (current != null)
        {
            if (File.Exists(Path.Combine(current.FullName, "CyanTools.csproj")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate the CyanTools repository root.");
    }

    private static void RunSta(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                action();
            }
            catch (Exception exception)
            {
                failure = exception;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure != null)
        {
            throw failure;
        }
    }

    private sealed class ObservableText : INotifyPropertyChanged
    {
        private string _value;

        public ObservableText(string value) => _value = value;

        public string Value
        {
            get => _value;
            set
            {
                if (_value == value)
                {
                    return;
                }

                _value = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Value)));
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
    }
}

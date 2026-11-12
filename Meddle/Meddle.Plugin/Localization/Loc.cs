using System.Globalization;
using System.Text.Json;

namespace Meddle.Plugin.Localization;

public enum UiLanguage { Auto, English, SimplifiedChinese }

/// <summary>Plugin UI resources. Never changes the culture used by exporters.</summary>
public static class Loc
{
    private static readonly Dictionary<string, string> English = Load("en");
    private static readonly Dictionary<string, string> Chinese = Load("zh-CN");
    private static volatile bool chinese;

    public static bool IsChinese => chinese;
    public static bool ResolveChinese(UiLanguage preference, string? hostLanguage) => preference switch
    {
        UiLanguage.SimplifiedChinese => true,
        UiLanguage.Auto => hostLanguage?.Split('-', '_')[0].Equals("zh", StringComparison.OrdinalIgnoreCase) == true,
        _ => false
    };

    public static void Refresh(UiLanguage preference, string? hostLanguage) => chinese = ResolveChinese(preference, hostLanguage);
    public static string Text(string key) => Get(key, chinese);
    public static string Get(string key, bool useChinese) =>
        useChinese && Chinese.TryGetValue(key, out var translated) ? translated : English.GetValueOrDefault(key, key);
    public static string Format(string key, params object?[] args) =>
        string.Format(CultureInfo.InvariantCulture, Text(key), args);
    // ### resets the ImGui hash, keeping identity independent of visible text.
    public static string Label(string key) => Text(key) + "###" + key;
    public static string LabelFormat(string key, params object?[] args) => Format(key, args) + "###" + key;
    public static ushort[] ChineseGlyphRanges => Chinese.Values.SelectMany(v => v)
        .Concat("简体中文跟随语言").Where(c => c > 127).Distinct().Order()
        .SelectMany(c => new[] { (ushort)c, (ushort)c }).Append((ushort)0).ToArray();

    private static Dictionary<string, string> Load(string language)
    {
        using var stream = typeof(Loc).Assembly.GetManifestResourceStream($"Meddle.Plugin.Localization.{language}.json")
            ?? throw new InvalidOperationException($"Missing UI resource: {language}");
        return JsonSerializer.Deserialize<Dictionary<string, string>>(stream)!;
    }
}

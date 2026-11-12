using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Meddle.Plugin.Localization;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Meddle.Utils.Tests;

[TestClass]
[DoNotParallelize]
public class LocalizationTests
{
    [TestMethod]
    public void LanguageSelectionRespectsExplicitChoiceAndHostFallback()
    {
        foreach (var host in new[] { "zh", "zh-CN", "zh_TW", "ZH-Hans" })
        {
            Assert.IsTrue(Loc.ResolveChinese(default, host));
            Assert.IsFalse(Loc.ResolveChinese(UiLanguage.English, host));
        }
        foreach (var host in new string?[] { null, "", "en", "ja", "de", "zhx" })
        {
            Assert.IsFalse(Loc.ResolveChinese(UiLanguage.Auto, host));
            Assert.IsTrue(Loc.ResolveChinese(UiLanguage.SimplifiedChinese, host));
        }
        Assert.IsFalse(Loc.ResolveChinese((UiLanguage)999, "zh"));
    }

    [TestMethod]
    public void ResourcesHaveMatchingKeysAndValidPlaceholders()
    {
        var english = Read("en");
        var chinese = Read("zh-CN");
        CollectionAssert.AreEquivalent(english.Keys.ToArray(), chinese.Keys.ToArray());
        foreach (var (key, value) in english)
        {
            Assert.IsFalse(string.IsNullOrWhiteSpace(chinese[key]), key);
            var original = CompositeFormat.Parse(value);
            var translated = CompositeFormat.Parse(chinese[key]);
            Assert.AreEqual(original.MinimumArgumentCount, translated.MinimumArgumentCount, key);
            CollectionAssert.AreEquivalent(Placeholders(value), Placeholders(chinese[key]), key);
            Assert.AreEqual(value.Count(c => c == '\0'), chinese[key].Count(c => c == '\0'), key);
            Assert.IsFalse(value.Contains("###") || chinese[key].Contains("###"), key);
        }
    }

    [TestMethod]
    public void SwitchingLanguagePreservesControlIdentityAndExportCulture()
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");
            Loc.Refresh(UiLanguage.English, "zh");
            var english = Loc.Label("Export");
            Loc.Refresh(UiLanguage.SimplifiedChinese, "en");
            var chinese = Loc.Label("Export");
            Assert.AreNotEqual(english, chinese);
            Assert.AreEqual(english.Split("###")[1], chinese.Split("###")[1]);
            Assert.AreEqual("de-DE", CultureInfo.CurrentCulture.Name);
            StringAssert.Contains(Loc.Format("CurrentOrigin", 1.5, 2.5, 3.5), "1.50");
            Assert.AreEqual("MissingResource", Loc.Get("MissingResource", true));
            Assert.AreEqual("Export", Loc.Get("Export", false));
            Assert.AreEqual("导出", Loc.Get("Export", true));
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
            Loc.Refresh(UiLanguage.Auto, "en");
        }
    }

    [TestMethod]
    public void FontRangesCoverAllTranslatedTextAndLanguageSelector()
    {
        var ranges = Loc.ChineseGlyphRanges;
        Assert.AreEqual((ushort)0, ranges[^1]);
        var glyphs = ranges.SkipLast(1).ToHashSet();
        foreach (var c in string.Concat(Read("zh-CN").Values) + "简体中文跟随语言")
            if (c > 127) Assert.IsTrue(glyphs.Contains(c), $"Missing glyph U+{(int)c:X4}");
    }

    private static Dictionary<string, string> Read(string language)
    {
        using var stream = typeof(Loc).Assembly.GetManifestResourceStream($"Meddle.Plugin.Localization.{language}.json")!;
        return JsonSerializer.Deserialize<Dictionary<string, string>>(stream)!;
    }

    private static string[] Placeholders(string text) => Regex.Matches(text, @"\{\d+(?:[^{}]*)\}")
        .Select(m => m.Value).ToArray();
}

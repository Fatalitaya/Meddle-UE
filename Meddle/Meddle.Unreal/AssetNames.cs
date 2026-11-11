using System.Text;
using System.Text.RegularExpressions;

namespace Meddle.Unreal;

public static class AssetNames
{
    public static string Sanitize(string name)
    {
        var value = Regex.Replace(name.Normalize(NormalizationForm.FormC), @"[^\p{L}\p{N}\p{M}_]+", "_").Trim('_');
        return value.Length == 0 ? "Character" : value;
    }

    internal static bool IsValid(string name) => Regex.IsMatch(name, @"\A[\p{L}\p{N}\p{M}_]+\z");
}

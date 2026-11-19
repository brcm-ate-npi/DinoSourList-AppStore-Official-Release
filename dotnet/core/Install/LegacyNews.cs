using System.Net;
using System.Text.RegularExpressions;

namespace DinoSaurList.Core.Install;

public static class LegacyNews
{
    // Retain the original HTML separately; import readable text without executing legacy markup.
    public static string PlainText(string html)
    {
        const RegexOptions options = RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.NonBacktracking;
        var text = Regex.Replace(html, @"<(script|style)\b[^>]*>.*?</(?:script|style)>", "", options);
        text = Regex.Replace(text, @"<br\s*/?>|</(?:p|div|li|tr|h[1-6])>", "\n", options);
        text = Regex.Replace(text, @"<[^>]*>", "", options);
        return WebUtility.HtmlDecode(text).Trim();
    }
}

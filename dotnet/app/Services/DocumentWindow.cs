using System.Diagnostics;
using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace DinoSaurList;

// Native, inert Markdown: no WebView, embedded scripts, HTML execution or remote tracking images.
public sealed class DocumentWindow : Window
{
    public DocumentWindow(string title, string markdown)
    {
        Title = title; Width = 900; Height = 700; MinWidth = 550; MinHeight = 400;
        var blocks = new StackPanel { Margin = new Thickness(28), Spacing = 8 };
        var code = false;
        foreach (var raw in markdown.Replace("\r", "").Split('\n').Take(10000))
        {
            var line = raw;
            if (line.StartsWith("```", StringComparison.Ordinal)) { code = !code; continue; }
            if (string.IsNullOrWhiteSpace(line)) { blocks.Children.Add(new Border { Height = 6 }); continue; }
            var heading = code ? 0 : line.TakeWhile(c => c == '#').Count();
            if (heading > 0) line = line[heading..].TrimStart();
            if (!code) line = Regex.Replace(line, @"!\[([^\]]*)\]\([^)]+\)", "$1");
            if (!code) line = line.Replace("**", "").Replace("__", "");
            var text = new SelectableTextBlock { Text = line, TextWrapping = TextWrapping.Wrap, FontSize = heading > 0 ? Math.Max(15, 27 - 2 * heading) : 14, FontWeight = heading > 0 ? FontWeight.SemiBold : FontWeight.Normal };
            if (code || line.Contains('|')) text.FontFamily = new FontFamily("Consolas, Menlo, monospace");
            blocks.Children.Add(text);
            if (!code)
                foreach (Match link in Regex.Matches(raw, @"(?<!!)\[([^\]]+)\]\((https?://[^\s)]+)\)"))
                {
                    var url = link.Groups[2].Value;
                    var button = new Button { Content = link.Groups[1].Value, HorizontalAlignment = HorizontalAlignment.Left };
                    button.Click += (_, _) => Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
                    blocks.Children.Add(button);
                }
        }
        Content = new ScrollViewer { Content = blocks, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled };
    }
}

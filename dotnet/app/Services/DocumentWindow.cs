using System.Diagnostics;
using System.Text.RegularExpressions;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;

namespace DinoSaurList;

// Native Markdown/HTML controls; no embedded browser or JavaScript execution.
public sealed class DocumentWindow : Window
{
    public DocumentWindow(string title, string markdown)
    {
        Title = title; Width = 900; Height = 700; MinWidth = 550; MinHeight = 400;
        Content = CreateViewer(markdown);
    }
    public static global::Markdown.Avalonia.Full.MarkdownScrollViewer CreateViewer(string markdown)
    {
        var safe = Regex.Replace(markdown, @"<(script|style|iframe|object)\b[^>]*>.*?</(?:script|style|iframe|object)>", "", RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.NonBacktracking);
        var viewer = new global::Markdown.Avalonia.Full.MarkdownScrollViewer
        {
            Margin = new Thickness(24), SelectionEnabled = true,
            Plugins = new global::Markdown.Avalonia.Full.MdAvPlugins { HyperlinkCommand = new WebLinkCommand() },
            Markdown = safe
        };
        return viewer;
    }
    private sealed class WebLinkCommand : ICommand
    {
        public event EventHandler? CanExecuteChanged { add { } remove { } }
        public bool CanExecute(object? parameter) => Uri.TryCreate(parameter?.ToString(), UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https";
        public void Execute(object? parameter)
        {
            if (CanExecute(parameter)) Process.Start(new ProcessStartInfo(parameter!.ToString()!) { UseShellExecute = true });
        }
    }
}

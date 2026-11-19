using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using DinoSaurList.Core.Assistant;
using DinoSaurList.ViewModels;

namespace DinoSaurList;

public sealed class AssistantWindow : Window
{
    public AssistantWindow(MainViewModel vm)
    {
        Title = "AI Assistant"; Width = 920; Height = 760; MinWidth = 560; MinHeight = 500;
        DataContext = vm; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Opened += (_, _) => { if (Screens.ScreenFromWindow(this) is { } screen && screen.WorkingArea.Height > 0) Height = Math.Min(Height, Math.Max(MinHeight, screen.WorkingArea.Height / screen.Scaling - 48)); };
        var messages = new ItemsControl { ItemsSource = vm.AssistantMessages, Margin = new Thickness(0,0,20,20),
            ItemTemplate = new FuncDataTemplate<ChatMessage>((item, _) => new Border
            {
                Background = Brush.Parse(item!.Role == "You" ? "#EEF3FF" : "#FFFFFF"), BorderBrush = Brush.Parse("#DAE2F0"), BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(9), Padding = new Thickness(16), Margin = new Thickness(0,0,0,12),
                Child = new StackPanel { Spacing = 8, Children =
                {
                    new TextBlock { Text = item.Role, FontWeight = FontWeight.SemiBold, Foreground = Brush.Parse("#4837C8") },
                    new SelectableTextBlock { Text = item.Text, Foreground = Brush.Parse("#25304A"), TextWrapping = TextWrapping.Wrap }
                }}
            }) };
        var history = new ScrollViewer { Name = "AssistantHistory", Content = messages, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled };
        var question = new TextBox { Name = "AssistantQuestion", PlaceholderText = "Ask about software tools… (Enter to send, Shift+Enter for a new line)", AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 64, MaxHeight = 140 };
        question.Bind(TextBox.TextProperty, new Binding(nameof(vm.Query)) { Mode = BindingMode.TwoWay });
        var send = new Button { Content = "Send", Command = vm.RecommendCommand, HorizontalAlignment = HorizontalAlignment.Stretch };
        send.Bind(IsEnabledProperty, new Binding(nameof(vm.Idle)));
        var close = new Button { Content = "Close", HorizontalAlignment = HorizontalAlignment.Stretch }; close.Click += (_, _) => Close();
        var input = new Grid { ColumnDefinitions = new ColumnDefinitions("*,100"), ColumnSpacing = 10, Margin = new Thickness(0,12,0,0) };
        input.Children.Add(question);
        var actions = new StackPanel { Spacing = 8, Children = { send, close } }; Grid.SetColumn(actions, 1); input.Children.Add(actions);
        question.KeyDown += (_, e) => { if (e.Key == Avalonia.Input.Key.Enter && !e.KeyModifiers.HasFlag(Avalonia.Input.KeyModifiers.Shift) && vm.Idle) { vm.RecommendCommand.Execute(null); e.Handled = true; } };
        var status = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0,8) };
        status.Bind(TextBlock.TextProperty, new Binding(nameof(vm.AssistantStatus)));
        var resultItems = new ItemsControl { ItemsSource = vm.Recommendations, Margin = new Thickness(0,0,20,16),
            ItemTemplate = new FuncDataTemplate<Recommendation>((item, _) => new Button { Content = new TextBlock { Text = item!.Tool.Name + " — View in Store", TextWrapping = TextWrapping.Wrap }, Command = vm.SelectRecommendationCommand, CommandParameter = item, HorizontalAlignment = HorizontalAlignment.Stretch, Margin = new Thickness(0,3) }) };
        var results = new ScrollViewer { Name = "AssistantRecommendations", Content = resultItems, MaxHeight = 160, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled };
        var reload = new Button { Content = "Reload README", Command = vm.IndexDocumentationCommand, HorizontalAlignment = HorizontalAlignment.Stretch, Margin = new Thickness(0,8,0,0) };
        reload.Bind(IsEnabledProperty, new Binding(nameof(vm.Idle)));
        var panel = new DockPanel { Margin = new Thickness(22) };
        var header = new StackPanel { Spacing = 8, Margin = new Thickness(0,0,0,16), Children = { new TextBlock { Text = "AI Assistant", FontSize = 26, FontWeight = FontWeight.Bold, Foreground = Brush.Parse("#4837C8") }, new TextBlock { Text = "Find the right tool using its README. Ask a question or browse all tools.", TextWrapping = TextWrapping.Wrap } } };
        DockPanel.SetDock(header, Dock.Top); panel.Children.Add(header);
        DockPanel.SetDock(reload, Dock.Bottom); panel.Children.Add(reload);
        DockPanel.SetDock(input, Dock.Bottom); panel.Children.Add(input);
        DockPanel.SetDock(status, Dock.Bottom); panel.Children.Add(status);
        DockPanel.SetDock(results, Dock.Bottom); panel.Children.Add(results);
        panel.Children.Add(history); Content = panel;
        System.Collections.Specialized.NotifyCollectionChangedEventHandler changed = (_, _) => Dispatcher.UIThread.Post(history.ScrollToEnd, DispatcherPriority.Background);
        vm.AssistantMessages.CollectionChanged += changed; Closed += (_, _) => vm.AssistantMessages.CollectionChanged -= changed;
        Opened += (_, _) => question.Focus();
    }
}

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Layout;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using DinoSaurList.Core.Assistant;
using DinoSaurList.ViewModels;

namespace DinoSaurList;

public sealed class AssistantWindow : Window
{
    public AssistantWindow(MainViewModel vm)
    {
        Title = "AI Assistant"; Width = 840; Height = 660; MinWidth = 560; MinHeight = 500;
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
        var history = new ScrollViewer { Name = "AssistantHistory", HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled };
        var question = new TextBox { Name = "AssistantQuestion", PlaceholderText = "Ask about software tools… (Enter to send, Shift+Enter for a new line)", AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, Height = 68, MinHeight = 68, VerticalContentAlignment = VerticalAlignment.Top };
        question.Bind(TextBox.TextProperty, new Binding(nameof(vm.Query)) { Mode = BindingMode.TwoWay });
        var send = new Button { Content = "Send", Command = vm.RecommendCommand, Height = 36, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Center, Background = Brush.Parse("#0866FF"), Foreground = Brushes.White };
        send.Bind(IsEnabledProperty, new Binding(nameof(vm.Idle)));
        var close = new Button { Content = "Close", Height = 36, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Center }; close.Click += (_, _) => Close();
        // TextBox handles Return itself. Tunnel first so plain Enter sends,
        // while Shift+Enter reaches the normal multiline editing behavior.
        question.AddHandler(KeyDownEvent, (_, e) =>
        {
            if (e.Key != Key.Enter || e.KeyModifiers.HasFlag(KeyModifiers.Shift)) return;
            e.Handled = true;
            if (vm.Idle && vm.RecommendCommand.CanExecute(null)) vm.RecommendCommand.Execute(null);
        }, RoutingStrategies.Tunnel);
        var status = new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 12, MaxLines = 2 };
        status.Bind(TextBlock.TextProperty, new Binding(nameof(vm.AssistantStatus)));
        var resultItems = new ItemsControl { ItemsSource = vm.Recommendations, Margin = new Thickness(0,0,20,8),
            ItemTemplate = new FuncDataTemplate<Recommendation>((item, _) => new Button { Content = new TextBlock { Text = item!.Tool.Name + " — View in Store", TextWrapping = TextWrapping.Wrap }, Command = vm.SelectRecommendationCommand, CommandParameter = item, HorizontalAlignment = HorizontalAlignment.Stretch, Margin = new Thickness(0,3) }) };
        var results = new ScrollViewer { Name = "AssistantRecommendations", Content = resultItems, MaxHeight = 160, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled };
        var reload = new Button { Content = "Reload README", Command = vm.IndexDocumentationCommand, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Center, Height = 28, MinHeight = 28, Padding = new Thickness(8,3) };
        reload.Bind(IsEnabledProperty, new Binding(nameof(vm.Idle)));
        // Recommendations belong to the scrollable conversation, never a fixed
        // bottom pane that takes space away from the chat history.
        var conversation = new StackPanel { Children = { messages, results } };
        history.Content = conversation;
        var footer = new Grid { RowDefinitions = new RowDefinitions("Auto,68,28"), ColumnDefinitions = new ColumnDefinitions("*,100"), RowSpacing = 6, ColumnSpacing = 10, Margin = new Thickness(0,10,0,0) };
        Grid.SetColumnSpan(status, 2); footer.Children.Add(status);
        Grid.SetRow(question, 1); footer.Children.Add(question);
        Grid.SetRow(reload, 2); footer.Children.Add(reload);
        var actions = new StackPanel { Spacing = 6, VerticalAlignment = VerticalAlignment.Center, Children = { send, close } };
        Grid.SetRow(actions, 1); Grid.SetRowSpan(actions, 2); Grid.SetColumn(actions, 1); footer.Children.Add(actions);
        var panel = new Grid { Margin = new Thickness(22), RowDefinitions = new RowDefinitions("Auto,*,Auto") };
        var header = new StackPanel { Spacing = 6, Margin = new Thickness(0,0,0,12), Children = { new TextBlock { Text = "AI Assistant", FontSize = 24, FontWeight = FontWeight.Bold, Foreground = Brush.Parse("#0866FF") }, new TextBlock { Text = "Ask about any software in the Store — I'll read the README files and help you.", TextWrapping = TextWrapping.Wrap } } };
        panel.Children.Add(header);
        var chatBorder = new Border { BorderBrush = Brush.Parse("#B7D3FF"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8), Padding = new Thickness(10), Child = history };
        Grid.SetRow(chatBorder, 1); panel.Children.Add(chatBorder);
        Grid.SetRow(footer, 2); panel.Children.Add(footer); Content = panel;
        System.Collections.Specialized.NotifyCollectionChangedEventHandler changed = (_, _) => Dispatcher.UIThread.Post(history.ScrollToEnd, DispatcherPriority.Background);
        vm.AssistantMessages.CollectionChanged += changed; Closed += (_, _) => vm.AssistantMessages.CollectionChanged -= changed;
        Opened += (_, _) => question.Focus();
    }
}

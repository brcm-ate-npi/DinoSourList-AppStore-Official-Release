using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Layout;
using Avalonia.Threading;
using DinoSaurList.Core.Assistant;
using DinoSaurList.ViewModels;

namespace DinoSaurList;

// A non-modal conversation window, as in the original Python application.
public sealed class AssistantWindow : Window
{
    public AssistantWindow(MainViewModel vm)
    {
        Title = "AI Assistant"; Width = 760; Height = 680; MinWidth = 520; MinHeight = 420;
        DataContext = vm;
        var transcript = new SelectableTextBlock { TextWrapping = Avalonia.Media.TextWrapping.Wrap, Margin = new Thickness(14,14,24,24) };
        transcript.Bind(TextBlock.TextProperty, new Binding(nameof(vm.AssistantConversation)));
        var history = new ScrollViewer { Content = transcript };
        var question = new TextBox { PlaceholderText = "Describe your task…", MinWidth = 280 };
        question.Bind(TextBox.TextProperty, new Binding(nameof(vm.Query)) { Mode = BindingMode.TwoWay });
        var send = new Button { Content = "Send", Command = vm.RecommendCommand };
        send.Bind(IsEnabledProperty, new Binding(nameof(vm.Idle)));
        var input = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 8, Margin = new Thickness(0,12,0,0) };
        input.Children.Add(question); Grid.SetColumn(send, 1); input.Children.Add(send);
        question.KeyDown += (_, e) => { if (e.Key == Avalonia.Input.Key.Enter && vm.Idle) { vm.RecommendCommand.Execute(null); e.Handled = true; } };
        var status = new TextBlock { TextWrapping = Avalonia.Media.TextWrapping.Wrap, Margin = new Thickness(0,8) };
        status.Bind(TextBlock.TextProperty, new Binding(nameof(vm.AssistantStatus)));
        var results = new ItemsControl { ItemsSource = vm.Recommendations, MaxHeight = 160,
            ItemTemplate = new FuncDataTemplate<Recommendation>((item, _) => new Button { Content = item!.Tool.Name + " — View in Store", Command = vm.SelectRecommendationCommand, CommandParameter = item, Margin = new Thickness(0,3) }) };
        var panel = new DockPanel { Margin = new Thickness(20) };
        DockPanel.SetDock(input, Dock.Bottom); panel.Children.Add(input);
        DockPanel.SetDock(status, Dock.Bottom); panel.Children.Add(status);
        DockPanel.SetDock(results, Dock.Bottom); panel.Children.Add(results);
        panel.Children.Add(history); Content = panel;
        System.ComponentModel.PropertyChangedEventHandler changed = (_, e) => { if (e.PropertyName == nameof(vm.AssistantConversation)) Dispatcher.UIThread.Post(history.ScrollToEnd); };
        vm.PropertyChanged += changed; Closed += (_, _) => vm.PropertyChanged -= changed;
        Opened += (_, _) => question.Focus();
    }
}

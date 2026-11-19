using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Layout;
using DinoSaurList.ViewModels;

namespace DinoSaurList;

// Main UI is created only after the startup update has completed or failed safely.
public sealed class StartupWindow : Window
{
    public StartupWindow(MainViewModel vm)
    {
        Title = "DinoSaurList"; Width = 480; Height = 210; CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        DataContext = vm;
        var status = new TextBlock { TextWrapping = Avalonia.Media.TextWrapping.Wrap };
        status.Bind(TextBlock.TextProperty, new Binding(nameof(vm.UpdateStatus)));
        Content = new StackPanel { Margin = new Thickness(28), Spacing = 18, Children =
        {
            new TextBlock { Text = "Starting DinoSaurList", FontSize = 22, FontWeight = Avalonia.Media.FontWeight.SemiBold },
            status, new ProgressBar { IsIndeterminate = true, Height = 5, HorizontalAlignment = HorizontalAlignment.Stretch }
        }};
    }
}

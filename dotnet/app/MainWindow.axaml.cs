using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using DinoSaurList.ViewModels;

namespace DinoSaurList;

public sealed partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        SizeChanged += (_, _) => { if (DataContext is MainViewModel model) model.WindowWidth = ClientSize.Width; };
        DataContextChanged += (_, _) =>
        {
            if (DataContext is not MainViewModel vm) return;
            vm.WindowWidth = ClientSize.Width > 0 ? ClientSize.Width : Width;
            vm.PhotoPicker = async () =>
            {
                var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
                {
                    Title = "Choose profile photo",
                    AllowMultiple = false,
                    FileTypeFilter = [new FilePickerFileType("Images") { Patterns = ["*.png", "*.jpg", "*.jpeg"] }]
                });
                return files.Count > 0 ? files[0].TryGetLocalPath() : null;
            };
            vm.DocumentRequested += async (title, markdown) => await new DocumentWindow(title, markdown).ShowDialog(this);
            vm.ConfirmRequested = async message =>
            {
                var confirm = new Window { Title = "Remove installed version", Width = 480, Height = 220, CanResize = false, WindowStartupLocation = WindowStartupLocation.CenterOwner };
                var remove = new Button { Content = "Remove", HorizontalAlignment = HorizontalAlignment.Right };
                var cancel = new Button { Content = "Cancel" };
                remove.Click += (_, _) => confirm.Close(true); cancel.Click += (_, _) => confirm.Close(false);
                confirm.Content = new StackPanel { Margin = new Thickness(24), Spacing = 18, Children = { new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap }, new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12, Children = { cancel, remove } } } };
                return await confirm.ShowDialog<bool>(this);
            };
        };
    }
}

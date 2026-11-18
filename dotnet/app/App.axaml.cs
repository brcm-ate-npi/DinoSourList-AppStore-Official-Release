using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using DinoSaurList.ViewModels;

namespace DinoSaurList;

public sealed partial class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);
    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var vm = new MainViewModel(Environment.GetEnvironmentVariable("DINOSAURLIST_DATA_DIR"));
            var window = new MainWindow { DataContext = vm };
            desktop.MainWindow = window;
            window.Opened += async (_, _) =>
            {
                await vm.InitializeAsync();
                if (NativeSmoke.OutputPrefix is not null)
                {
                    try { await NativeSmoke.CaptureAsync(window, vm); desktop.Shutdown(); }
                    catch (Exception error)
                    {
                        await File.WriteAllTextAsync(NativeSmoke.OutputPrefix + ".error.txt", error.ToString()); desktop.Shutdown(1);
                    }
                }
            };
            desktop.Exit += (_, _) => vm.Dispose();
        }
        base.OnFrameworkInitializationCompleted();
    }
}

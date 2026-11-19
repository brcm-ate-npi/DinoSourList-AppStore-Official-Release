using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
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
            async Task LoadMainAsync(MainWindow window)
            {
                await vm.InitializeAsync();
                if (NativeSmoke.OutputPrefix is null)
                {
                    _ = vm.LoadLocalIdentityAsync();
                    _ = vm.RefreshNewsCommand.ExecuteAsync(null);
                    _ = vm.PreloadDeveloperProfilesAsync();
                }
                if (NativeSmoke.OutputPrefix is not null)
                {
                    try { await NativeSmoke.CaptureAsync(window, vm); desktop.Shutdown(); }
                    catch (Exception error)
                    {
                        await File.WriteAllTextAsync(NativeSmoke.OutputPrefix + ".error.txt", error.ToString()); desktop.Shutdown(1);
                    }
                }
            }
            if (NativeSmoke.OutputPrefix is not null)
            {
                var window = new MainWindow { DataContext = vm };
                desktop.MainWindow = window;
                window.Opened += async (_, _) => await LoadMainAsync(window);
            }
            else
            {
                var splash = new StartupWindow(vm);
                desktop.MainWindow = splash;
                splash.Opened += async (_, _) =>
                {
                    if (await vm.PrepareStartupUpdateAsync()) return;
                    if (!splash.IsVisible) return;
                    var window = new MainWindow { DataContext = vm };
                    desktop.MainWindow = window;
                    window.Opened += async (_, _) => await LoadMainAsync(window);
                    window.Show(); splash.Close();
                };
            }
            var updateTimer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(15) };
            updateTimer.Tick += (_, _) => { if (vm.Idle && !vm.RestartReady) _ = vm.PrepareAutomaticUpdateAsync(); };
            if (NativeSmoke.OutputPrefix is null) updateTimer.Start();
            desktop.Exit += (_, _) => { updateTimer.Stop(); vm.Dispose(); };
        }
        base.OnFrameworkInitializationCompleted();
    }
}

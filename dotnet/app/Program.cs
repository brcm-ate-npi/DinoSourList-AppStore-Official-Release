using Avalonia;
using Velopack;

namespace DinoSaurList;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        VelopackApp.Build().Run();
        var smokeIndex = Array.IndexOf(args, "--smoke-test-output");
        if (smokeIndex >= 0)
        {
            if (smokeIndex + 1 >= args.Length) throw new ArgumentException("--smoke-test-output requires a file prefix.");
            NativeSmoke.OutputPrefix = args[smokeIndex + 1];
        }
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>().UsePlatformDetect().WithInterFont().LogToTrace();
}

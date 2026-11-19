using Avalonia;
using Velopack;

namespace DinoSaurList;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        VelopackApp.Build().Run();
        var dataIndex = Array.IndexOf(args, "--data-dir");
        if (dataIndex >= 0)
        {
            if (dataIndex + 1 >= args.Length) throw new ArgumentException("--data-dir requires a directory.");
            Environment.SetEnvironmentVariable("DINOSAURLIST_DATA_DIR", Path.GetFullPath(args[dataIndex + 1]));
        }
        var smokeIndex = Array.IndexOf(args, "--smoke-test-output");
        if (smokeIndex >= 0)
        {
            if (smokeIndex + 1 >= args.Length) throw new ArgumentException("--smoke-test-output requires a file prefix.");
            NativeSmoke.OutputPrefix = args[smokeIndex + 1];
        }
        try { BuildAvaloniaApp().StartWithClassicDesktopLifetime(args); }
        catch (Exception error) when (NativeSmoke.OutputPrefix is { } prefix)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(prefix))!);
            File.WriteAllText(prefix + ".error.txt", error.ToString());
            Environment.ExitCode = 1;
        }
    }
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>().UsePlatformDetect().WithInterFont().LogToTrace();
}

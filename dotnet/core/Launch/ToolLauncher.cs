using System.Diagnostics;
using DinoSaurList.Core.Install;

namespace DinoSaurList.Core.Launch;

public interface IToolLauncher
{
    Task RunAsync(string root, string relative, bool wait, CancellationToken token);
}
public sealed class ToolLauncher(Func<ProcessStartInfo, Process?>? startProcess = null) : IToolLauncher
{
    public static string Platform => OperatingSystem.IsWindows() ? "windows" : OperatingSystem.IsMacOS() ? "macos" : "linux";
    public static ProcessStartInfo StartInfo(string root, string relative, string platform, bool wait)
    {
        var target = SafePaths.Within(root, relative);
        if (!File.Exists(target) && !Directory.Exists(target)) throw new FileNotFoundException("Tool execution file was not found.", target);
        var extension = Path.GetExtension(target).ToLowerInvariant();
        var info = new ProcessStartInfo { WorkingDirectory = Path.GetDirectoryName(target)!, UseShellExecute = false };
        if (platform == "windows")
        {
            if (extension == ".ps1") { info.FileName = "powershell.exe"; info.ArgumentList.Add("-NoProfile"); info.ArgumentList.Add("-File"); info.ArgumentList.Add(target); }
            else if (extension == ".msi") { info.FileName = "msiexec.exe"; info.ArgumentList.Add("/i"); info.ArgumentList.Add(target); info.ArgumentList.Add("/norestart"); }
            else { info.FileName = target; info.UseShellExecute = true; }
        }
        else if (extension is ".exe" or ".cmd" or ".bat" or ".msi" or ".lnk" or ".ps1")
            throw new PlatformNotSupportedException("This execution target requires Windows.");
        else if (extension is ".sh" or ".command") { info.FileName = "/bin/bash"; info.ArgumentList.Add(target); }
        else if (platform == "macos") { info.FileName = "/usr/bin/open"; if (wait) info.ArgumentList.Add("-W"); info.ArgumentList.Add("--"); info.ArgumentList.Add(target); }
        else { info.FileName = target; info.UseShellExecute = true; }
        return info;
    }
    public async Task RunAsync(string root, string relative, bool wait, CancellationToken token)
    {
        // ShellExecute, file associations and antivirus checks may block before returning a process.
        using var process = await Task.Run(() => (startProcess ?? Process.Start)(StartInfo(root, relative, Platform, wait))
            ?? throw new IOException("The application could not be started."), token).ConfigureAwait(false);
        if (!wait) return;
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(TimeSpan.FromMinutes(15));
        try { await process.WaitForExitAsync(deadline.Token); }
        catch (OperationCanceledException) { try { process.Kill(true); } catch (InvalidOperationException) { } throw; }
        if (process.ExitCode is not (0 or 1641 or 3010)) throw new IOException($"Installer exited with code {process.ExitCode}.");
    }
}

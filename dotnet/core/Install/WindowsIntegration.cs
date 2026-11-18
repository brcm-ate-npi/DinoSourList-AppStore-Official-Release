using System.Diagnostics;
using System.Runtime.InteropServices;
using DinoSaurList.Core.Registry;

namespace DinoSaurList.Core.Install;

public interface IInstallIntegration
{
    Task BeforeInstallAsync(ToolEntry tool, InstalledTool? previous, CancellationToken token);
    Task AfterInstallAsync(ToolEntry tool, string directory, string execution, CancellationToken token);
    Task BeforeRemoveAsync(InstalledTool tool, CancellationToken token);
}

/// <summary>Legacy Windows MSI replacement, Flow Delete and local shortcut contract.</summary>
public sealed class WindowsIntegration : IInstallIntegration
{
    public async Task BeforeInstallAsync(ToolEntry tool, InstalledTool? previous, CancellationToken token)
    {
        if (!OperatingSystem.IsWindows() || previous is null) return;
        var installer = tool.Flow.Get("installation", "file");
        if (!tool.Flow.Flag("installation", "auto") || !installer.EndsWith(".msi", StringComparison.OrdinalIgnoreCase)) return;
        var oldInstaller = previous.Flow?.Get("installation", "file", installer) ?? installer;
        var oldMsi = SafePaths.Within(previous.Directory, oldInstaller);
        if (File.Exists(oldMsi)) await RunAsync("msiexec.exe", ["/x", oldMsi, "/passive", "/norestart"], token, [0, 1605, 1641, 3010]);
        else await DeleteAsync(previous.Flow ?? tool.Flow, token);
    }
    public Task AfterInstallAsync(ToolEntry tool, string directory, string execution, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (!OperatingSystem.IsWindows() || string.IsNullOrEmpty(execution) || Directory.EnumerateFiles(directory, "*.lnk").Any()) return Task.CompletedTask;
        var target = SafePaths.Within(directory, execution);
        if (!File.Exists(target)) return Task.CompletedTask;
        object? shell = null; object? shortcut = null;
        try
        {
            shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell") ?? throw new IOException("Windows Script Host is unavailable."));
            dynamic host = shell!;
            shortcut = host.CreateShortcut(Path.Combine(directory, "DinoSaurList.lnk"));
            dynamic link = shortcut;
            link.TargetPath = target; link.WorkingDirectory = directory;
            link.IconLocation = Directory.EnumerateFiles(directory, "*.ico").FirstOrDefault() ?? target;
            link.Save();
        }
        finally
        {
            if (shortcut is not null && Marshal.IsComObject(shortcut)) Marshal.FinalReleaseComObject(shortcut);
            if (shell is not null && Marshal.IsComObject(shell)) Marshal.FinalReleaseComObject(shell);
        }
        return Task.CompletedTask;
    }
    public Task BeforeRemoveAsync(InstalledTool tool, CancellationToken token) => OperatingSystem.IsWindows() && tool.Flow is not null ? DeleteAsync(tool.Flow, token) : Task.CompletedTask;
    private static Task DeleteAsync(FlowDocument flow, CancellationToken token)
    {
        var id = flow.Get("delete", "idname");
        return flow.Flag("delete", "auto") && !string.IsNullOrWhiteSpace(id)
            ? RunAsync("winget.exe", ["uninstall", id, "--silent", "--accept-source-agreements"], token, [0]) : Task.CompletedTask;
    }
    private static async Task RunAsync(string executable, string[] arguments, CancellationToken token, int[] success)
    {
        var info = new ProcessStartInfo(executable) { UseShellExecute = false };
        foreach (var argument in arguments) info.ArgumentList.Add(argument);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token); deadline.CancelAfter(TimeSpan.FromMinutes(15));
        using var process = Process.Start(info) ?? throw new IOException("Cannot start " + executable);
        try { await process.WaitForExitAsync(deadline.Token); }
        catch (OperationCanceledException) { try { process.Kill(true); } catch (InvalidOperationException) { } throw; }
        if (!success.Contains(process.ExitCode)) throw new IOException($"{executable} exited with code {process.ExitCode}; installation records were preserved.");
    }
}

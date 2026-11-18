namespace DinoSaurList.Core.Install;

public static class SafePaths
{
    public static string Within(string root, string relative)
    {
        if (string.IsNullOrWhiteSpace(relative) || relative.IndexOf('\0') >= 0) throw new InvalidDataException("A relative file path is required.");
        var clean = relative.Replace('\\', '/');
        if (clean.StartsWith('/') || clean.Contains(':') || clean.Split('/').Any(p => p == ".."))
            throw new InvalidDataException("File path escapes its tool directory.");
        var fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var full = Path.GetFullPath(Path.Combine(fullRoot, clean));
        if (!full.StartsWith(fullRoot, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
            throw new InvalidDataException("File path escapes its tool directory.");
        // Check the root and target themselves as well as intermediate directories.
        var rootInfo = new DirectoryInfo(Path.GetFullPath(root));
        if (rootInfo.Exists && rootInfo.LinkTarget is not null) throw new InvalidDataException("Linked tool roots are not allowed.");
        if (Directory.Exists(full) && new DirectoryInfo(full).LinkTarget is not null) throw new InvalidDataException("Linked directories are not allowed.");
        // Existing links cannot redirect installer/launcher/import operations outside the owned tree.
        for (var parent = Path.GetDirectoryName(full); parent is not null && parent.Length >= fullRoot.Length; parent = Path.GetDirectoryName(parent))
            if (Directory.Exists(parent) && new DirectoryInfo(parent).LinkTarget is not null) throw new InvalidDataException("Linked directories are not allowed.");
        if (File.Exists(full) && new FileInfo(full).LinkTarget is not null) throw new InvalidDataException("Linked files are not allowed.");
        return full;
    }
    public static string ToolId(string id) => id.Length is > 0 and <= 30 && id.All(char.IsAsciiDigit) ? id : throw new InvalidDataException("Invalid tool ID.");
}

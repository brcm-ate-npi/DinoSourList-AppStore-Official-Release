namespace DinoSaurList.Core.Registry;

/// <summary>Serializes read/modify/write operations across CLI and desktop processes.</summary>
internal static class StateLease
{
    public static async Task<FileStream> AcquireAsync(AppStorage storage, string name, CancellationToken token)
    {
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (true)
        {
            token.ThrowIfCancellationRequested();
            try { return new FileStream(Path.Combine(storage.Root, name + ".lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
            catch (IOException) when (DateTime.UtcNow < deadline) { await Task.Delay(25, token); }
        }
    }
}

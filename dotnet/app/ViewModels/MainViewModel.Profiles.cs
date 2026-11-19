using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DinoSaurList.Core.Profile;

namespace DinoSaurList.ViewModels;

public sealed partial class MainViewModel
{
    private sealed record ProfileSnapshot(DeveloperProfile? Profile, string? Photo);
    private readonly Dictionary<string, Task<ProfileSnapshot>> profileLoads = new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim profileRequests = new(2, 2);
    private int profileEpoch;
    public Task ProfileDisplayTask { get; private set; } = Task.CompletedTask;
    [ObservableProperty] private string profileStatus = "";

    // Only reads shared profiles. It never takes the global tool-operation Busy flag.
    public async Task PreloadDeveloperProfilesAsync()
    {
        var epoch = profileEpoch;
        var authors = Authors.Where(a => !string.IsNullOrWhiteSpace(a) && a.Length <= 100).OrderBy(a => a == ProfileName ? 0 : 1).ToArray();
        foreach (var batch in authors.Chunk(2))
        {
            if (epoch != profileEpoch || updateCancellation.IsCancellationRequested) return;
            await Task.WhenAll(batch.Select(author => ReadProfileSafelyAsync(author)));
        }
    }
    private Task<ProfileSnapshot> ReadProfileSafelyAsync(string author, bool refresh = false)
    {
        if (refresh && profileLoads.TryGetValue(author, out var old) && old.IsCompleted) profileLoads.Remove(author);
        if (profileLoads.TryGetValue(author, out var pending)) return pending;
        var service = profiles;
        async Task<ProfileSnapshot> ReadAsync()
        {
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(updateCancellation.Token);
            deadline.CancelAfter(TimeSpan.FromSeconds(20));
            var acquired = false;
            try
            {
                await profileRequests.WaitAsync(deadline.Token); acquired = true;
                var profile = await service.LoadAsync(author, deadline.Token);
                var photo = await service.LoadPhotoAsync(author, deadline.Token);
                return new(profile, photo);
            }
            catch (Exception error) when (error is not OutOfMemoryException)
            {
                if (!updateCancellation.IsCancellationRequested) Log($"Profile {author}: {error.Message}");
                return await CachedProfileAsync(author, service);
            }
            finally { if (acquired) profileRequests.Release(); }
        }
        return profileLoads[author] = ReadAsync();
    }
    private async Task<ProfileSnapshot> CachedProfileAsync(string author, DeveloperProfiles service)
    {
        var photoFolder = Path.Combine(Storage.Root, "Photos");
        var name = DeveloperProfiles.SafeName(author);
        var photo = new[] { Path.Combine(photoFolder, name + ".pending.png"), Path.Combine(photoFolder, name + ".local.png") }.FirstOrDefault(File.Exists);
        if (photo is null && Directory.Exists(photoFolder)) photo = Directory.EnumerateFiles(photoFolder, name + "-*.png").OrderByDescending(File.GetLastWriteTimeUtc).FirstOrDefault();
        try { return new(await service.LoadCacheAsync(author, updateCancellation.Token), photo); }
        catch (Exception error) when (error is IOException or System.Text.Json.JsonException or OperationCanceledException) { return new(null, photo); }
    }
    private void ApplyProfile(string author, ProfileSnapshot snapshot)
    {
        if (ProfileName != author || EditingProfile || updateCancellation.IsCancellationRequested) return;
        ProfileMessage = snapshot.Profile?.Tweet ?? "";
        ProfileLinksText = string.Join('\n', snapshot.Profile?.Links.Select(l => l.Title + " | " + l.Url + (string.IsNullOrEmpty(l.Key) ? "" : " | " + l.Key)) ?? []);
        Bitmap? image = null;
        try { if (snapshot.Photo is not null) image = new Bitmap(snapshot.Photo); }
        catch (Exception error) when (error is IOException or ArgumentException) { Log("Profile image: " + error.Message); }
        ProfilePhoto?.Dispose(); ProfilePhoto = image;
    }
    private async Task ShowProfileAsync(bool refresh)
    {
        var author = ProfileName;
        if (string.IsNullOrWhiteSpace(author)) return;
        var epoch = profileEpoch;
        try
        {
            var cached = await CachedProfileAsync(author, profiles);
            if (epoch != profileEpoch) return;
            if (cached.Profile is not null) ApplyProfile(author, cached);
            if (ProfileName == author) ProfileStatus = "Loading shared profile…";
            var snapshot = await ReadProfileSafelyAsync(author, refresh);
            if (epoch != profileEpoch) return;
            ApplyProfile(author, snapshot);
            if (ProfileName == author) ProfileStatus = snapshot.Profile is null ? "Shared profile unavailable." : "Shared profile ready.";
        }
        catch (OperationCanceledException) { }
        catch (Exception error) when (error is not OutOfMemoryException) { Log("Profile: " + error.Message); }
    }
    [RelayCommand] private Task LoadSharedProfileAsync() => ShowProfileAsync(true);
}

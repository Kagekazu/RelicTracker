namespace RelicTracker.Framework;

public sealed class FfxivCollectService
{
    private readonly object gate = new();
    private bool isLoading;
    private DateTime? lastAttemptUtc;

    public FfxivCollectSnapshot Snapshot { get; private set; } = FfxivCollectSnapshot.Empty;

    public bool IsLoading
    {
        get
        {
            lock (gate)
            {
                return isLoading;
            }
        }
    }

    public string? StatusMessage { get; private set; }

    public DateTime? LastRefreshUtc { get; private set; }

    public void Refresh(ulong characterId)
    {
        if (characterId == 0)
        {
            StatusMessage = "Enter your FFXIV Collect character ID.";
            return;
        }

        lock (gate)
        {
            if (isLoading)
            {
                return;
            }

            isLoading = true;
            lastAttemptUtc = DateTime.UtcNow;
        }

        StatusMessage = "Fetching from FFXIV Collect…";

        Task.Run(async () =>
        {
            try
            {
                FfxivCollectSnapshot snapshot = await FfxivCollectClient.FetchCharacterRelicsAsync(characterId).ConfigureAwait(false);
                lock (gate)
                {
                    Snapshot = snapshot;
                    LastRefreshUtc = DateTime.UtcNow;
                    StatusMessage = null;
                }

                Svc.Log.Information(
                    "[RelicTracker] FFXIV Collect: {Owned} owned, {Missing} missing for character {CharacterId}.",
                    snapshot.Owned.Count,
                    snapshot.Missing.Count,
                    characterId);
            }
            catch (FfxivCollectException ex)
            {
                StatusMessage = ex.Message;
                Svc.Log.Warning("[RelicTracker] FFXIV Collect: {Message}", ex.Message);
            }
            catch (TaskCanceledException)
            {
                StatusMessage = "FFXIV Collect timed out. Allagan Tools inventory progress still works — try Refresh again later.";
                Svc.Log.Warning("[RelicTracker] FFXIV Collect timed out for character {CharacterId}.", characterId);
            }
            catch (Exception ex)
            {
                StatusMessage = "Could not reach FFXIV Collect. Allagan Tools inventory progress still works.";
                Svc.Log.Warning(ex, "[RelicTracker] FFXIV Collect request failed.");
            }
            finally
            {
                lock (gate)
                {
                    isLoading = false;
                }
            }
        });
    }

    public void RefreshIfStale(ulong characterId, TimeSpan maxAge)
    {
        if (characterId == 0 || IsLoading)
        {
            return;
        }

        DateTime now = DateTime.UtcNow;
        if (lastAttemptUtc is not null && now - lastAttemptUtc.Value < maxAge)
        {
            return;
        }

        if (LastRefreshUtc is not null && now - LastRefreshUtc.Value < maxAge)
        {
            return;
        }

        Refresh(characterId);
    }
}

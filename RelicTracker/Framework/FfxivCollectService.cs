namespace RelicTracker.Framework;

public sealed class FfxivCollectService
{
    private readonly object gate = new();
    private bool isLoading;
    private DateTime? lastAttemptUtc;
    private int refreshGeneration;

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

    public void Refresh(ulong characterId) => Refresh(characterId, force: false);

    /// <summary>Starts a new fetch even if one is already in progress (supersedes stale requests).</summary>
    public void ForceRefresh(ulong characterId) => Refresh(characterId, force: true);

    private void Refresh(ulong characterId, bool force)
    {
        if (characterId == 0)
        {
            StatusMessage = "Enter your FFXIV Collect character ID.";
            return;
        }

        int generation;
        lock (gate)
        {
            if (isLoading && !force)
            {
                return;
            }

            isLoading = true;
            lastAttemptUtc = DateTime.UtcNow;
            generation = ++refreshGeneration;
        }

        StatusMessage = "Fetching from FFXIV Collect…";

        Task.Run(async () =>
        {
            try
            {
                FfxivCollectSnapshot snapshot = await FfxivCollectClient.FetchCharacterRelicsAsync(characterId).ConfigureAwait(false);
                lock (gate)
                {
                    if (generation != refreshGeneration)
                    {
                        return;
                    }

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
                lock (gate)
                {
                    if (generation != refreshGeneration)
                    {
                        return;
                    }

                    StatusMessage = ex.Message;
                }

                Svc.Log.Warning("[RelicTracker] FFXIV Collect: {Message}", ex.Message);
            }
            catch (TaskCanceledException)
            {
                lock (gate)
                {
                    if (generation != refreshGeneration)
                    {
                        return;
                    }

                    StatusMessage = "FFXIV Collect timed out. Allagan Tools inventory progress still works — try Recheck again later.";
                }

                Svc.Log.Warning("[RelicTracker] FFXIV Collect timed out for character {CharacterId}.", characterId);
            }
            catch (Exception ex)
            {
                lock (gate)
                {
                    if (generation != refreshGeneration)
                    {
                        return;
                    }

                    StatusMessage = "Could not reach FFXIV Collect. Allagan Tools inventory progress still works.";
                }

                Svc.Log.Warning(ex, "[RelicTracker] FFXIV Collect request failed.");
            }
            finally
            {
                lock (gate)
                {
                    if (generation == refreshGeneration)
                    {
                        isLoading = false;
                    }
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

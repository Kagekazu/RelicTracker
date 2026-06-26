namespace RelicTracker.Framework;

public sealed class FfxivCollectService
{
    private readonly object gate = new();
    private bool isLoading;

    public FfxivCollectSnapshot Snapshot { get; private set; } = FfxivCollectSnapshot.Empty;

    public bool IsLoading
    {
        get
        {
            lock(gate)
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

        lock(gate)
        {
            if (isLoading)
            {
                return;
            }

            isLoading = true;
        }

        StatusMessage = "Fetching from FFXIV Collect…";

        Task.Run(async () =>
        {
            try
            {
                FfxivCollectSnapshot snapshot = await FfxivCollectClient.FetchCharacterRelicsAsync(characterId).ConfigureAwait(false);
                lock(gate)
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
            catch(FfxivCollectException ex)
            {
                StatusMessage = ex.Message;
                Svc.Log.Warning("[RelicTracker] FFXIV Collect: {Message}", ex.Message);
            }
            catch(Exception ex)
            {
                StatusMessage = "Could not reach FFXIV Collect.";
                Svc.Log.Warning(ex, "[RelicTracker] FFXIV Collect request failed.");
            }
            finally
            {
                lock(gate)
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

        if (LastRefreshUtc is null || DateTime.UtcNow - LastRefreshUtc.Value > maxAge)
        {
            Refresh(characterId);
        }
    }
}

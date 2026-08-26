using System.Net;
using System.Net.Http;

namespace RelicTracker.Framework;

public sealed class FfxivCollectSnapshot
{
    public static FfxivCollectSnapshot Empty { get; } = new();

    public ulong CharacterId { get; init; }
    public List<FfxivCollectRelic> Owned { get; init; } = [];
    public List<FfxivCollectRelic> Missing { get; init; } = [];
}

public sealed class FfxivCollectRelic
{
    [JsonPropertyName("order")]
    public int Order { get; set; }

    [JsonPropertyName("type")]
    public FfxivCollectRelicType? Type { get; set; }
}

public sealed class FfxivCollectRelicType
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;
}

internal sealed class FfxivCollectApiError
{
    [JsonPropertyName("error")]
    public string? Error { get; set; }
}

public sealed class FfxivCollectException(string message) : Exception(message);

internal static class FfxivCollectClient
{
    private const string BaseUrl = "https://ffxivcollect.com/api";

    private static readonly HttpClient Http = new()
    {
        Timeout = TimeSpan.FromSeconds(90)
    };

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        NumberHandling = JsonNumberHandling.AllowReadingFromString
    };

    static FfxivCollectClient() => Http.DefaultRequestHeaders.UserAgent.ParseAdd("RelicTracker/0.1");

    public static async Task<FfxivCollectSnapshot> FetchCharacterRelicsAsync(ulong characterId)
    {
        var ownedTask = FetchRelicListAsync($"{BaseUrl}/characters/{characterId}/relics/owned");
        var missingTask = FetchRelicListAsync($"{BaseUrl}/characters/{characterId}/relics/missing");
        await Task.WhenAll(ownedTask, missingTask).ConfigureAwait(false);

        return new()
        {
            CharacterId = characterId,
            Owned = await ownedTask.ConfigureAwait(false),
            Missing = await missingTask.ConfigureAwait(false)
        };
    }

    private static async Task<List<FfxivCollectRelic>> FetchRelicListAsync(string url)
    {
        using var response = await Http.GetAsync(url).ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            throw new FfxivCollectException(ParseErrorMessage(response.StatusCode, body));
        }

        if (string.IsNullOrWhiteSpace(body))
        {
            return [];
        }

        return ParseRelicList(body);
    }

    private static List<FfxivCollectRelic> ParseRelicList(string body)
    {
        using var document = JsonDocument.Parse(body);
        var root = document.RootElement;

        if (root.ValueKind == JsonValueKind.Array)
        {
            return DeserializeRelicList(root.GetRawText());
        }

        if (root.ValueKind == JsonValueKind.Object
            && root.TryGetProperty("results", out var results)
            && results.ValueKind == JsonValueKind.Array)
        {
            return DeserializeRelicList(results.GetRawText());
        }

        throw new FfxivCollectException("Unexpected response format from FFXIV Collect.");
    }

    private static List<FfxivCollectRelic> DeserializeRelicList(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<List<FfxivCollectRelic>>(json, JsonOptions) ?? [];
        }
        catch (JsonException ex)
        {
            throw new FfxivCollectException($"Could not parse relic data from FFXIV Collect ({ex.Message}).");
        }
    }

    private static string ParseErrorMessage(HttpStatusCode statusCode, string body)
    {
        try
        {
            var error = JsonSerializer.Deserialize<FfxivCollectApiError>(body, JsonOptions);
            if (!string.IsNullOrWhiteSpace(error?.Error))
            {
                return error.Error;
            }
        }
        catch
        {
        }

        return statusCode switch
        {
            HttpStatusCode.NotFound => "Character not found on FFXIV Collect.",
            HttpStatusCode.Forbidden => "Character or relic collection is private on FFXIV Collect.",
            var _ => $"FFXIV Collect request failed ({(int)statusCode})."
        };
    }
}

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

    public void Refresh(ulong characterId) => Refresh(characterId, false);

    public void ForceRefresh(ulong characterId) => Refresh(characterId, true);

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
                var snapshot = await FfxivCollectClient.FetchCharacterRelicsAsync(characterId).ConfigureAwait(false);
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

        var now = DateTime.UtcNow;
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

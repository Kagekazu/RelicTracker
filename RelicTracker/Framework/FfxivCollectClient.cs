using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace RelicTracker.Framework;

internal static class FfxivCollectClient
{
    private const string BaseUrl = "https://ffxivcollect.com/api";

    private static readonly HttpClient Http = new()
    {
        Timeout = TimeSpan.FromSeconds(30),
    };

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
    };

    static FfxivCollectClient()
    {
        Http.DefaultRequestHeaders.UserAgent.ParseAdd("RelicTracker/0.1");
    }

    public static async Task<FfxivCollectSnapshot> FetchCharacterRelicsAsync(ulong characterId)
    {
        var owned = await FetchRelicListAsync($"{BaseUrl}/characters/{characterId}/relics/owned");
        var missing = await FetchRelicListAsync($"{BaseUrl}/characters/{characterId}/relics/missing");

        return new FfxivCollectSnapshot
        {
            CharacterId = characterId,
            Owned = owned,
            Missing = missing,
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
            // ignore parse failure
        }

        return statusCode switch
        {
            HttpStatusCode.NotFound => "Character not found on FFXIV Collect.",
            HttpStatusCode.Forbidden => "Character or relic collection is private on FFXIV Collect.",
            _ => $"FFXIV Collect request failed ({(int)statusCode}).",
        };
    }
}

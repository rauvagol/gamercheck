using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace GamerCheck.FFLogs;

/// <summary>
/// Calls FFLogs public API (client credentials) to fetch character rankings/parses.
/// </summary>
public sealed class FFLogsApiService
{
    private const string TokenUrl = "https://www.fflogs.com/oauth/token";
    private const string ClientApiUrl = "https://www.fflogs.com/api/v2/client";

    private readonly string _clientId;
    private readonly string _clientSecret;
    private readonly HttpClient _http = new();
    private string? _cachedToken;
    private DateTime _tokenExpiry = DateTime.MinValue;

    public FFLogsApiService(string clientId, string clientSecret)
    {
        _clientId = clientId ?? "";
        _clientSecret = clientSecret ?? "";
    }

    public bool HasCredentials => !string.IsNullOrWhiteSpace(_clientId) && !string.IsNullOrWhiteSpace(_clientSecret);

    /// <summary>Region for API: NA, JP, EU, OC (uppercase).</summary>
    public static string ToApiRegion(string fflogsRegionLower)
    {
        return fflogsRegionLower?.ToLowerInvariant() switch
        {
            "na" => "NA",
            "jp" => "JP",
            "eu" => "EU",
            "oc" => "OC",
            _ => "NA"
        };
    }

    public async Task<string?> GetAccessTokenAsync()
    {
        if (!HasCredentials) return null;
        if (_cachedToken != null && DateTime.UtcNow < _tokenExpiry)
            return _cachedToken;

        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Post, TokenUrl);
            req.Headers.TryAddWithoutValidation("Cache-Control", "no-cache");
            req.Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "client_credentials",
                ["client_id"] = _clientId,
                ["client_secret"] = _clientSecret
            });

            using var res = await _http.SendAsync(req).ConfigureAwait(false);
            res.EnsureSuccessStatusCode();
            var json = await res.Content.ReadAsStringAsync().ConfigureAwait(false);
            var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (!root.TryGetProperty("access_token", out var tok))
                return null;
            _cachedToken = tok.GetString();
            var expiresIn = root.TryGetProperty("expires_in", out var exp) ? exp.GetInt32() : 3600;
            _tokenExpiry = DateTime.UtcNow.AddSeconds(expiresIn - 60);
            return _cachedToken;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Fetches rankings (encounter name + best rdps) for one character, same data as the character page. Returns result with error message on failure.</summary>
    public async Task<FFLogsRankingsResult> GetCharacterRankingsAsync(string serverRegion, string serverSlug, string characterName)
    {
        if (!HasCredentials)
            return FFLogsRankingsResult.Fail("API credentials not set.");
        var token = await GetAccessTokenAsync().ConfigureAwait(false);
        if (string.IsNullOrEmpty(token))
            return FFLogsRankingsResult.Fail("Could not get API token. Check Client ID and Secret.");

        // zoneRankings is type JSON - no sub-selection allowed. We get raw JSON and parse it.
        var query = """
            query characterData($name: String!, $serverSlug: String!, $serverRegion: String!, $zoneId: Int!) {
              characterData {
                character(name: $name, serverSlug: $serverSlug, serverRegion: $serverRegion) {
                  hidden
                  zoneRankings(zoneID: $zoneId)
                }
              }
            }
            """;
        var variables = new Dictionary<string, object>
        {
            ["name"] = characterName,
            ["serverSlug"] = serverSlug,
            ["serverRegion"] = serverRegion,
            ["zoneId"] = 73
        };
        var body = new { query, variables };
        var json = JsonSerializer.Serialize(body);

        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Post, ClientApiUrl);
            req.Headers.TryAddWithoutValidation("Authorization", "Bearer " + token);
            req.Content = new StringContent(json, Encoding.UTF8, "application/json");

            using var res = await _http.SendAsync(req).ConfigureAwait(false);
            var content = await res.Content.ReadAsStringAsync().ConfigureAwait(false);
            var doc = JsonDocument.Parse(content);
            var data = doc.RootElement;

            if (data.TryGetProperty("errors", out var err) && err.GetArrayLength() > 0)
            {
                var msg = err[0].TryGetProperty("message", out var m) ? m.GetString() : err[0].GetRawText();
                return FFLogsRankingsResult.Fail(msg ?? "API error");
            }

            if (!data.TryGetProperty("data", out var charData) || !charData.TryGetProperty("characterData", out var cd) || !cd.TryGetProperty("character", out var ch))
                return FFLogsRankingsResult.Fail("Invalid API response.");
            if (ch.ValueKind == JsonValueKind.Null)
                return FFLogsRankingsResult.Fail("Character not found. Check name/server/region.");
            if (ch.TryGetProperty("hidden", out var hidden) && hidden.GetBoolean())
                return FFLogsRankingsResult.Success(new List<FFLogsRankingEntry> { new() { EncounterName = "(hidden)", Rdps = 0 } });

            var list = new List<FFLogsRankingEntry>();
            if (!ch.TryGetProperty("zoneRankings", out var zr) || zr.ValueKind == JsonValueKind.Null)
                return FFLogsRankingsResult.Success(list);

            // zoneRankings is type JSON - may be string (parse it) or object
            if (zr.ValueKind == JsonValueKind.String)
            {
                var raw = zr.GetString();
                if (string.IsNullOrEmpty(raw)) return FFLogsRankingsResult.Success(list);
                try
                {
                    using var sub = JsonDocument.Parse(raw);
                    ParseRankingsInto(sub.RootElement, list);
                }
                catch
                {
                    // ignore parse error
                }
            }
            else
            {
                ParseRankingsInto(zr, list);
            }
            return FFLogsRankingsResult.Success(list);
        }
        catch (Exception ex)
        {
            return FFLogsRankingsResult.Fail(ex.Message);
        }
    }

    private static void ParseRankingsInto(JsonElement rankingsRoot, List<FFLogsRankingEntry> list)
    {
        if (!rankingsRoot.TryGetProperty("rankings", out var rankings))
            return;
        foreach (var r in rankings.EnumerateArray())
        {
            var encounterName = "?";
            if (r.TryGetProperty("encounter", out var enc) && enc.TryGetProperty("name", out var en))
                encounterName = en.GetString() ?? "?";
            var bestAmount = 0.0;
            if (r.TryGetProperty("bestAmount", out var amt))
                bestAmount = amt.ValueKind == JsonValueKind.Number ? amt.GetDouble() : 0;
            var spec = "";
            if (r.TryGetProperty("bestSpec", out var specEl))
                spec = specEl.GetString() ?? "";
            list.Add(new FFLogsRankingEntry
            {
                EncounterName = encounterName,
                Rdps = (long)Math.Round(bestAmount),
                Spec = spec
            });
        }
    }

    public void InvalidateToken()
    {
        _cachedToken = null;
        _tokenExpiry = DateTime.MinValue;
    }
}

public class FFLogsRankingEntry
{
    public string EncounterName { get; set; } = "";
    public long Rdps { get; set; }
    public string Spec { get; set; } = "";
}

public class FFLogsRankingsResult
{
    public bool IsSuccess => Error == null;
    public string? Error { get; private set; }
    public List<FFLogsRankingEntry>? Parses { get; private set; }

    public static FFLogsRankingsResult Success(List<FFLogsRankingEntry> parses) => new() { Parses = parses };
    public static FFLogsRankingsResult Fail(string error) => new() { Error = error };
}

using System.Globalization;
using System.Net;
using System.Text.Json;

namespace HomeControl.Api.Blink;

// Clips vom USB-Stick am Sync Module. Am echten Konto belegt, siehe docs/BLINK_CLIPS.md.
// Löscht nie etwas: Der Stick bleibt unverändert.
public partial class BlinkHttpCameraClient
{
    // Die Liste weckt das Sync Module. Deshalb nur selten neu anfordern.
    private static readonly TimeSpan ClipListCacheDuration = TimeSpan.FromMinutes(5);

    // Clip-Kennungen vom Stick gelten nur kurz. Ältere Listen werden vor dem Abspielen erneuert.
    private static readonly TimeSpan ClipRefMaxAge = TimeSpan.FromMinutes(10);
    private const int ManifestPolls = 10;
    private const int ClipDownloadAttempts = 4;
    private const int MaxClipBytes = 60 * 1024 * 1024;

    // Im Test ohne echte Wartezeit.
    public TimeSpan ClipPollInterval { get; init; } = TimeSpan.FromSeconds(2);

    // Die Adresse eines Clips bleibt im Backend. Der Browser kennt nur den kurzen Schlüssel.
    private sealed record ClipRef(long NetworkId, string Path);

    private sealed record SyncModule(string Id, long NetworkId, bool? LocalEnabled, string? LocalStatus);

    private readonly SemaphoreSlim clipListLock = new(1, 1);

    // Das Sync Module stellt immer nur einen Clip zur Zeit bereit.
    private readonly SemaphoreSlim clipVideoLock = new(1, 1);
    private Dictionary<string, ClipRef> clipRefs = [];
    private BlinkClipList? lastClipList;
    private DateTimeOffset lastClipListUtc = DateTimeOffset.MinValue;
    private IReadOnlyList<SyncModule> lastSyncModules = [];

    public async Task<BlinkClipList> GetClipsAsync(bool refresh, CancellationToken cancellationToken)
    {
        if (!options.CurrentValue.Enabled)
        {
            throw new BlinkDisabledException();
        }

        await clipListLock.WaitAsync(cancellationToken);
        try
        {
            if (!refresh && lastClipList is not null && timeProvider.GetUtcNow() - lastClipListUtc < ClipListCacheDuration)
            {
                return lastClipList;
            }

            await EnsureFreshAsync(cancellationToken);
            var refs = new Dictionary<string, ClipRef>();
            var clips = new List<BlinkClip>();
            var problem = await LoadLocalClipsAsync(clips, refs, cancellationToken);

            clipRefs = refs;
            lastClipList = new BlinkClipList(clips.OrderByDescending(c => c.CreatedAtUtc).ToList(), problem);
            lastClipListUtc = timeProvider.GetUtcNow();
            return lastClipList;
        }
        finally
        {
            clipListLock.Release();
        }
    }

    private async Task<string?> LoadLocalClipsAsync(List<BlinkClip> clips, Dictionary<string, ClipRef> refs, CancellationToken cancellationToken)
    {
        var modules = lastSyncModules.Where(m => m.LocalStatus == "active" || m.LocalEnabled == true).ToList();
        if (modules.Count == 0)
        {
            return "Das Sync Module meldet keinen aktiven lokalen Speicher.";
        }

        string? problem = null;
        foreach (var module in modules)
        {
            try
            {
                var current = await GetSessionAsync(forceRenew: false, cancellationToken);
                var basePath = $"/api/v1/accounts/{current.AccountId}/networks/{module.NetworkId}/sync_modules/{module.Id}/local_storage/manifest";
                long requestId;
                using (var request = await PostJsonAsync(basePath + "/request", null, cancellationToken))
                {
                    if (ReadCommandId(request.RootElement) is not { } id)
                    {
                        problem = "Das Sync Module hat keine Anfrage-Nummer für die Clip-Liste geliefert.";
                        continue;
                    }
                    requestId = id;
                }

                await WaitForCommandAsync(module.NetworkId, requestId, cancellationToken);
                using var manifest = await PollManifestAsync($"{basePath}/request/{requestId}", cancellationToken);
                if (manifest is null)
                {
                    problem = "Das Sync Module hat die Clip-Liste nicht rechtzeitig bereitgestellt.";
                    continue;
                }

                var root = manifest.RootElement;
                var manifestId = ScalarText(root, "manifest_id");
                if (manifestId is null || !manifestId.All(char.IsAsciiLetterOrDigit))
                {
                    problem = "Die Clip-Liste vom Sync Module hat keine gültige Kennung.";
                    continue;
                }

                foreach (var item in root.GetProperty("clips").EnumerateArray())
                {
                    var id = ScalarText(item, "id");
                    if (id is null || !id.All(char.IsAsciiLetterOrDigit)) continue;
                    if (!DateTimeOffset.TryParse(GetString(item, "created_at"), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var created)) continue;

                    var key = "l" + id;
                    refs[key] = new ClipRef(module.NetworkId, $"{basePath}/{manifestId}/clip/request/{id}");
                    clips.Add(new BlinkClip(key, GetString(item, "camera_name") ?? "Unbekannte Kamera", created.ToUniversalTime()));
                }
            }
            catch (Exception ex) when (ex is BlinkApiException or HttpRequestException or JsonException or KeyNotFoundException or InvalidOperationException
                                         || (ex is TaskCanceledException && !cancellationToken.IsCancellationRequested))
            {
                problem = ex is BlinkApiException ? ex.Message : "Das Sync Module war nicht erreichbar.";
            }
        }
        return problem;
    }

    private async Task<JsonDocument?> PollManifestAsync(string path, CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < ManifestPolls; attempt++)
        {
            try
            {
                var document = await GetJsonAsync(path, cancellationToken);
                if (document.RootElement.ValueKind == JsonValueKind.Object
                    && document.RootElement.TryGetProperty("clips", out var clips) && clips.ValueKind == JsonValueKind.Array)
                {
                    return document;
                }
                document.Dispose();
            }
            catch (BlinkApiException)
            {
                // Solange das Sync Module noch hochlädt, antwortet Blink mit einem Fehler.
            }
            await Task.Delay(ClipPollInterval, timeProvider, cancellationToken);
        }
        return null;
    }

    public async Task<BlinkClipVideo?> GetClipVideoAsync(string key, CancellationToken cancellationToken)
    {
        if (!options.CurrentValue.Enabled)
        {
            throw new BlinkDisabledException();
        }

        await clipVideoLock.WaitAsync(cancellationToken);
        try
        {
            // Nach einem Neustart oder bei einer alten Liste zuerst frische Kennungen holen.
            if (!clipRefs.ContainsKey(key) || timeProvider.GetUtcNow() - lastClipListUtc > ClipRefMaxAge)
            {
                await GetClipsAsync(refresh: true, cancellationToken);
            }
            if (!clipRefs.TryGetValue(key, out var clip))
            {
                return null;
            }

            try
            {
                return await LoadClipAsync(clip, cancellationToken);
            }
            catch (BlinkApiException)
            {
                // Vielleicht ist die Kennung schon verfallen: genau einmal mit neuer Liste versuchen.
                await GetClipsAsync(refresh: true, cancellationToken);
                return clipRefs.TryGetValue(key, out var renewed) ? await LoadClipAsync(renewed, cancellationToken) : null;
            }
        }
        finally
        {
            clipVideoLock.Release();
        }
    }

    private async Task<BlinkClipVideo> LoadClipAsync(ClipRef clip, CancellationToken cancellationToken)
    {
        // Das Sync Module stellt den Clip kurz über Blink bereit. Auf dem Stick bleibt er unverändert.
        using (var request = await PostJsonAsync(clip.Path, null, cancellationToken))
        {
            if (ReadCommandId(request.RootElement) is { } command)
            {
                await WaitForCommandAsync(clip.NetworkId, command, cancellationToken);
            }
        }

        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return await DownloadClipAsync(clip.Path, cancellationToken);
            }
            catch (BlinkApiException) when (attempt < ClipDownloadAttempts)
            {
                await Task.Delay(ClipPollInterval, timeProvider, cancellationToken);
            }
        }
    }

    private async Task<BlinkClipVideo> DownloadClipAsync(string path, CancellationToken cancellationToken)
    {
        var current = await GetSessionAsync(forceRenew: false, cancellationToken);
        using var first = await SendThumbnailRequestAsync(current, path, cancellationToken);
        if (first.StatusCode != HttpStatusCode.Unauthorized)
        {
            return await ReadClipAsync(first, cancellationToken);
        }

        var renewed = await GetSessionAsync(forceRenew: true, cancellationToken);
        using var second = await SendThumbnailRequestAsync(renewed, path, cancellationToken);
        if (second.StatusCode == HttpStatusCode.Unauthorized)
        {
            MarkLoginRequired(renewed.RefreshToken);
            throw new BlinkLoginRequiredException("Blink hat die Anmeldung abgelehnt. Bitte tools/BlinkSpike erneut ausführen.");
        }
        return await ReadClipAsync(second, cancellationToken);
    }

    private static async Task<BlinkClipVideo> ReadClipAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (!response.IsSuccessStatusCode)
        {
            throw new BlinkApiException($"Blink hat den Clip nicht geliefert (HTTP {(int)response.StatusCode}).");
        }
        if (response.Content.Headers.ContentLength is > MaxClipBytes)
        {
            throw new BlinkApiException("Der Clip ist unerwartet groß.");
        }

        var content = await response.Content.ReadAsByteArrayAsync(cancellationToken);
        if (content.Length > MaxClipBytes || !IsMp4(content))
        {
            throw new BlinkApiException("Blink hat keine MP4-Datei geliefert.");
        }
        return new BlinkClipVideo(content);
    }

    // Eine MP4-Datei trägt ab Byte 4 die Kennung "ftyp".
    public static bool IsMp4(ReadOnlySpan<byte> content) =>
        content.Length > 12 && content[4..8].SequenceEqual("ftyp"u8);

    private async Task<JsonDocument> GetJsonAsync(string path, CancellationToken cancellationToken)
    {
        var current = await GetSessionAsync(forceRenew: false, cancellationToken);
        using var first = await SendAsync(HttpMethod.Get, current, path, cancellationToken);
        if (first.StatusCode != HttpStatusCode.Unauthorized)
        {
            return await ReadJsonAsync(first, cancellationToken);
        }

        var renewed = await GetSessionAsync(forceRenew: true, cancellationToken);
        using var second = await SendAsync(HttpMethod.Get, renewed, path, cancellationToken);
        if (second.StatusCode == HttpStatusCode.Unauthorized)
        {
            MarkLoginRequired(renewed.RefreshToken);
            throw new BlinkLoginRequiredException("Blink hat die Anmeldung abgelehnt. Bitte tools/BlinkSpike erneut ausführen.");
        }
        return await ReadJsonAsync(second, cancellationToken);
    }

    private static string? ScalarText(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value)
            ? value.ValueKind switch
            {
                JsonValueKind.String => value.GetString(),
                JsonValueKind.Number => value.GetRawText(),
                _ => null,
            }
            : null;

    private static List<SyncModule> MapSyncModules(JsonElement homescreen)
    {
        var modules = new List<SyncModule>();
        if (!homescreen.TryGetProperty("sync_modules", out var list) || list.ValueKind != JsonValueKind.Array) return modules;

        foreach (var item in list.EnumerateArray())
        {
            if (GetIdText(item) is not { } id || !item.TryGetProperty("network_id", out var network) || !network.TryGetInt64(out var networkId)) continue;
            bool? enabled = item.TryGetProperty("local_storage_enabled", out var value) && value.ValueKind is JsonValueKind.True or JsonValueKind.False
                ? value.GetBoolean()
                : null;
            modules.Add(new SyncModule(id, networkId, enabled, GetString(item, "local_storage_status")));
        }
        return modules;
    }
}

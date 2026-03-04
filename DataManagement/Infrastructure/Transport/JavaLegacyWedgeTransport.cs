using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

using WAD.Runner.Application.Ports;
using WAD.Runner.DataManagement.Infrastructure.Transport.Dtos;

namespace WAD.Runner.DataManagement.Infrastructure.Transport;

/// <summary>
/// Adapter: talks to the OLD Java API (partspec + specrows-by-ps),
/// but returns the NEW transport DTOs expected by JavaWedgeDataSource/WedgeDataAssembler.
/// </summary>
public sealed class JavaLegacyWedgeTransport : IJavaWedgeTransport
{
    private readonly HttpClient _http;
    private readonly int _firma;
    private readonly string _language;

    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    public JavaLegacyWedgeTransport(HttpClient http, int firma, string language = "E")
    {
        _http = http ?? throw new ArgumentNullException(nameof(http));
        _firma = firma;
        _language = string.IsNullOrWhiteSpace(language) ? "E" : language.Trim();
    }

    // ==============================
    // OLD Java API response DTOs
    // ==============================

    private sealed class PartSpecResp
    {
        [JsonPropertyName("ok")] public bool Ok { get; set; }
        [JsonPropertyName("partspec")] public string? PartSpec { get; set; }
    }

    private sealed class ArtDescResp
    {
        [JsonPropertyName("ok")] public bool Ok { get; set; }
        [JsonPropertyName("description")] public string? Description { get; set; }
    }

    private sealed class SpecRowDto
    {
        [JsonPropertyName("template")] public string? Template { get; set; }
        [JsonPropertyName("xRow")] public string? XRow { get; set; }
        [JsonPropertyName("columnId")] public string? ColumnId { get; set; }
    }

    private sealed class SpecRowsResp
    {
        [JsonPropertyName("ok")] public bool Ok { get; set; }
        [JsonPropertyName("rows")] public List<SpecRowDto> Rows { get; set; } = new();
    }

    // ==============================
    // IJavaWedgeTransport (NEW surface)
    // ==============================

    public async Task<WedSpec1Dto> GetWedSpec1Async(string article, CancellationToken ct)
    {
        var (_, rows) = await GetPartSpecAndRowsAsync(article, ct);
        var spec1 = rows.Where(r => Eq(r.Template, "Wed-Spec1")).ToList();

        return new WedSpec1Dto(
            ArticleNumber: article,

            WedPolish: Get(spec1, "Wed-Polish"),
            WedPS: Get(spec1, "Wed-PS"),
            WedNotes: Get(spec1, "Wed-Notes"),
            WedOverlay: Get(spec1, "Wed-Overlay"),

            WedEngrave: Get(spec1, "Wed-Engrave"),
            WedCoining: Get(spec1, "Wed-Coining"),

            WedType: Get(spec1, "Wed-Type"),
            WedFootOption: Get(spec1, "Wed-Foot_Option"),
            WedWireExit: Get(spec1, "Wed-Wire_Exit"),
            WedFeedHSlot: Get(spec1, "Wed-Feed_H/Slot"),

            DwgText1: Get(spec1, "Wed-Dwg-Text1"),
            DwgText2: Get(spec1, "Wed-Dwg-Text2"),
            DwgText3: Get(spec1, "Wed-Dwg-Text3"),
            DwgText4: Get(spec1, "Wed-Dwg-Text4"),
            DwgText5: Get(spec1, "Wed-Dwg-Text5"),
            DwgText6: Get(spec1, "Wed-Dwg-Text6"),
            DwgText7: Get(spec1, "Wed-Dwg-Text7")
        );
    }

    public async Task<IReadOnlyList<WedSpec2RowDto>> GetWedSpec2Async(string article, CancellationToken ct)
    {
        var (_, rows) = await GetPartSpecAndRowsAsync(article, ct);
        var spec2 = rows.Where(r => Eq(r.Template, "Wed-Spec2")).ToList();

        // Match your SqliteWedgeDataSource behavior:
        // return all rows except Wed_K-Value (handled by GetWedKValueAsync)
        return spec2
            .Where(r => !Eq(r.XRow, "Wed_K-Value"))
            .Select(r => new WedSpec2RowDto(Key: r.XRow ?? string.Empty, Payload: r.ColumnId ?? string.Empty))
            .ToList();
    }

    public async Task<WedKValueDto?> GetWedKValueAsync(string article, CancellationToken ct)
    {
        var (_, rows) = await GetPartSpecAndRowsAsync(article, ct);
        var spec2 = rows.Where(r => Eq(r.Template, "Wed-Spec2")).ToList();

        var raw = spec2.FirstOrDefault(r => Eq(r.XRow, "Wed_K-Value"))?.ColumnId;
        return string.IsNullOrWhiteSpace(raw) ? null : new WedKValueDto(raw);
    }

    public async Task<IReadOnlyList<WedMarkingRowDto>> GetWedMarkingAsync(string article, CancellationToken ct)
    {
        // In your old repo you said marking rows can come from:
        // 1) base specrows-by-ps (if Java injects them)
        // 2) dedicated endpoint marking-specrows-by-ps
        // We'll do both and merge.

        var (ps, rows) = await GetPartSpecAndRowsAsync(article, ct);

        var baseMarking = rows
            .Where(r => Eq(r.Template, "Wed-Marking"))
            .Select(r => new WedMarkingRowDto(
                XRow: r.XRow ?? string.Empty,
                Text: string.IsNullOrWhiteSpace(r.ColumnId) ? null : r.ColumnId))
            .ToList();

        var extra = await TryGetMarkingRowsByPsAsync(ps, ct);

        // Merge by XRow (case-insensitive), prefer extra if duplicate
        var dict = new Dictionary<string, WedMarkingRowDto>(StringComparer.OrdinalIgnoreCase);
        foreach (var m in baseMarking) dict[m.XRow] = m;
        foreach (var m in extra) dict[m.XRow] = m;

        return dict.Values.ToList();
    }

    public async Task<PgbSpec1Dto> GetPgbSpec1Async(string article, CancellationToken ct)
    {
        var (_, rows) = await GetPartSpecAndRowsAsync(article, ct);
        var spec1 = rows.Where(r => Eq(r.Template, "PGB-Spec1")).ToList();

        return new PgbSpec1Dto(
            ArticleNumber: article,
            Polish: Get(spec1, "PGB-Polish"),
            PS: Get(spec1, "PGB-PS"),
            Remarks: Get(spec1, "PGB-Remarks"),

            Engrave: Get(spec1, "Wed-Engrave"),
            FLBlank: Get(spec1, "Wed-FL-Blank"),

            DwgText1: Get(spec1, "Wed-Dwg-Text1"),
            DwgText2: Get(spec1, "Wed-Dwg-Text2"),
            DwgText3: Get(spec1, "Wed-Dwg-Text3"),
            DwgText4: Get(spec1, "Wed-Dwg-Text4"),
            DwgText5: Get(spec1, "Wed-Dwg-Text5"),
            DwgText6: Get(spec1, "Wed-Dwg-Text6"),
            DwgText7: Get(spec1, "Wed-Dwg-Text7"),

            WedType: Get(spec1, "Wed-Type"),
            WedFootOption: Get(spec1, "Wed-Foot_Option"),
            WedWireExit: Get(spec1, "Wed-Wire_Exit"),
            WedFeedHSlot: Get(spec1, "Wed-Feed_H/Slot")
        );
    }

    public async Task<IReadOnlyList<PgbSpec2RowDto>> GetPgbSpec2Async(string article, CancellationToken ct)
    {
        var (_, rows) = await GetPartSpecAndRowsAsync(article, ct);
        var spec2 = rows.Where(r => Eq(r.Template, "PGB-Spec2")).ToList();

        return spec2
            .Select(r => new PgbSpec2RowDto(Key: r.XRow ?? string.Empty, Payload: r.ColumnId ?? string.Empty))
            .ToList();
    }

    // ==============================
    // Core: OLD endpoints
    // ==============================

    private async Task<(string partSpec, List<SpecRowDto> rows)> GetPartSpecAndRowsAsync(string article, CancellationToken ct)
    {
        var partSpec = await GetPartSpecAsync(article, ct)
                       ?? throw new InvalidOperationException($"Java legacy API returned no PartSpec for article '{article}'.");

        var rows = await GetSpecRowsByPsAsync(partSpec, ct);
        return (partSpec, rows);
    }

    private async Task<string?> GetPartSpecAsync(string article, CancellationToken ct)
    {
        var url = $"api/dbprobe/partspec?firma={_firma}&article={Uri.EscapeDataString(article)}";
        using var resp = await _http.GetAsync(url, ct);
        await EnsureSuccessWithBodyHint(resp, url, ct);

        await using var s = await resp.Content.ReadAsStreamAsync(ct);
        var dto = await JsonSerializer.DeserializeAsync<PartSpecResp>(s, JsonOpts, ct);
        return dto?.PartSpec;
    }

    private async Task<List<SpecRowDto>> GetSpecRowsByPsAsync(string partSpec, CancellationToken ct)
    {
        var url = $"api/dbprobe/specrows-by-ps?firma={_firma}&partSpec={Uri.EscapeDataString(partSpec)}";
        using var resp = await _http.GetAsync(url, ct);
        await EnsureSuccessWithBodyHint(resp, url, ct);

        await using var s = await resp.Content.ReadAsStreamAsync(ct);
        var dto = await JsonSerializer.DeserializeAsync<SpecRowsResp>(s, JsonOpts, ct) ?? new SpecRowsResp();

        // Normalize nulls
        foreach (var r in dto.Rows)
        {
            r.Template ??= string.Empty;
            r.XRow ??= string.Empty;
            r.ColumnId ??= string.Empty;
        }

        return dto.Rows;
    }

    private async Task<List<WedMarkingRowDto>> TryGetMarkingRowsByPsAsync(string partSpec, CancellationToken ct)
    {
        var url = $"api/dbprobe/marking-specrows-by-ps?firma={_firma}&partSpec={Uri.EscapeDataString(partSpec)}";
        using var resp = await _http.GetAsync(url, ct);

        if (resp.StatusCode == HttpStatusCode.NotFound) return new List<WedMarkingRowDto>();
        if (!resp.IsSuccessStatusCode) return new List<WedMarkingRowDto>();

        await using var s = await resp.Content.ReadAsStreamAsync(ct);
        var dto = await JsonSerializer.DeserializeAsync<SpecRowsResp>(s, JsonOpts, ct) ?? new SpecRowsResp();

        return dto.Rows
            .Select(r => new WedMarkingRowDto(
                XRow: r.XRow ?? string.Empty,
                Text: string.IsNullOrWhiteSpace(r.ColumnId) ? null : r.ColumnId))
            .ToList();
    }

    // ==============================
    // Helpers
    // ==============================

    private static string? Get(List<SpecRowDto> rows, string xRowKey)
        => rows.FirstOrDefault(r => Eq(r.XRow, xRowKey))?.ColumnId;

    private static bool Eq(string? a, string b)
        => string.Equals(a?.Trim(), b, StringComparison.OrdinalIgnoreCase);

    private static async Task EnsureSuccessWithBodyHint(HttpResponseMessage resp, string url, CancellationToken ct)
    {
        if (resp.IsSuccessStatusCode) return;

        string body = string.Empty;
        try { body = await resp.Content.ReadAsStringAsync(ct); } catch { /* ignore */ }

        throw new HttpRequestException(
            $"Java legacy API returned {(int)resp.StatusCode} {resp.ReasonPhrase} for GET {url}"
            + (string.IsNullOrWhiteSpace(body) ? "" : $"\nBody:\n{Truncate(body, 800)}"));
    }

    private static string Truncate(string s, int max)
        => s.Length <= max ? s : s.Substring(0, max) + "…";
}
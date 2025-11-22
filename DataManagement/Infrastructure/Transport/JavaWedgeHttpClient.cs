using System.Net.Http.Json;
using WAD.Runner.Application.Ports;
using WAD.Runner.DataManagement.Infrastructure.Transport.Dtos;

namespace WAD.Runner.DataManagement.Infrastructure.Transport;

/// <summary>
/// Raw Java API client (transport layer). Returns transport DTOs only.
/// Higher layers map DTOs → domain models.
/// </summary>
public sealed class JavaWedgeHttpClient : IJavaWedgeTransport
{
    private readonly HttpClient _http;

    public JavaWedgeHttpClient(HttpClient http)
    {
        _http = http;
        // Optional: ensure JSON is the default Accept
        if (!_http.DefaultRequestHeaders.Accept.Any(h => h.MediaType == "application/json"))
            _http.DefaultRequestHeaders.Accept.ParseAdd("application/json");
    }

    // -------------------- FG / Wed-* --------------------

    public async Task<WedSpec1Dto> GetWedSpec1Async(string article, CancellationToken ct)
        => await GetRequiredAsync<WedSpec1Dto>($"api/dbprobe/wed/spec1?article={Uri.EscapeDataString(article)}", ct);

    public async Task<IReadOnlyList<WedSpec2RowDto>> GetWedSpec2Async(string article, CancellationToken ct)
        => await GetRequiredAsync<IReadOnlyList<WedSpec2RowDto>>($"api/dbprobe/wed/spec2?article={Uri.EscapeDataString(article)}", ct);

    public async Task<WedKValueDto?> GetWedKValueAsync(string article, CancellationToken ct)
        => await GetOptionalAsync<WedKValueDto>($"api/dbprobe/wed/kvalue?article={Uri.EscapeDataString(article)}", ct);

    public async Task<IReadOnlyList<WedMarkingRowDto>> GetWedMarkingAsync(string article, CancellationToken ct)
        => await GetRequiredAsync<IReadOnlyList<WedMarkingRowDto>>($"api/dbprobe/wed/marking?article={Uri.EscapeDataString(article)}", ct);

    // -------------------- PGB / PGB-* --------------------

    public async Task<PgbSpec1Dto> GetPgbSpec1Async(string article, CancellationToken ct)
        => await GetRequiredAsync<PgbSpec1Dto>($"api/dbprobe/pgb/spec1?article={Uri.EscapeDataString(article)}", ct);

    public async Task<IReadOnlyList<PgbSpec2RowDto>> GetPgbSpec2Async(string article, CancellationToken ct)
        => await GetRequiredAsync<IReadOnlyList<PgbSpec2RowDto>>($"api/dbprobe/pgb/spec2?article={Uri.EscapeDataString(article)}", ct);

    // -------------------- Helpers --------------------

    private async Task<T> GetRequiredAsync<T>(string relativeUrl, CancellationToken ct)
    {
        using var resp = await _http.GetAsync(relativeUrl, ct);
        if (!resp.IsSuccessStatusCode)
            throw new HttpRequestException($"Java API returned {(int)resp.StatusCode} for GET {relativeUrl}");

        var payload = await resp.Content.ReadFromJsonAsync<T>(cancellationToken: ct);
        if (payload is null)
            throw new InvalidOperationException($"Java API returned empty body for GET {relativeUrl}");

        return payload;
    }

    private async Task<T?> GetOptionalAsync<T>(string relativeUrl, CancellationToken ct)
    {
        using var resp = await _http.GetAsync(relativeUrl, ct);
        if (resp.StatusCode == System.Net.HttpStatusCode.NotFound) return default;
        if (!resp.IsSuccessStatusCode)
            throw new HttpRequestException($"Java API returned {(int)resp.StatusCode} for GET {relativeUrl}");

        return await resp.Content.ReadFromJsonAsync<T>(cancellationToken: ct);
    }
}

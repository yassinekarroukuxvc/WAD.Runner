using System.Data;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace WAD.Runner.DataManagement.Infrastructure.Sqlite;

/// <summary>
/// Minimal SQLite repository for mock/local runs.
/// Auto-detects whether values are stored in a separate XS_PartSpecValues table
/// or directly in XS_PartSpecSpecs.Column_ID.
/// </summary>
public sealed class ProAlphaRepository
{
    private readonly string _connString;
    private readonly ILogger<ProAlphaRepository> _log;

    // schema flags (lazy detected)
    private bool? _hasValuesTable;

    public ProAlphaRepository(string connString, ILogger<ProAlphaRepository>? log = null)
    {
        if (string.IsNullOrWhiteSpace(connString)) throw new ArgumentNullException(nameof(connString));
        _connString = connString;
        _log = log ?? NullLogger<ProAlphaRepository>.Instance;
    }

    private SqliteConnection Open()
    {
        var cn = new SqliteConnection(_connString);
        cn.Open();
        using var pragma = cn.CreateCommand();
        pragma.CommandText = "PRAGMA foreign_keys = ON;";
        pragma.ExecuteNonQuery();
        return cn;
    }

    /// <summary>Opens a connection for diagnostics callers. Caller must dispose.</summary>
    public SqliteConnection OpenConnection() => Open();

    // ---------------- NEW: Article description ----------------
    /// <summary>
    /// Returns localized article description (S_ArtikelSpr.Bezeichnung).
    /// </summary>
    public async Task<string?> GetArticleDescriptionAsync(int firma, string articleNumber, string language, CancellationToken ct = default)
    {
        const string sql = @"
            SELECT Bezeichnung
            FROM S_ArtikelSpr
            WHERE Firma = $firma AND Sprache = $sprache AND Artikel = $artikel
            LIMIT 1;";

        await using var cn = Open();
        await using var cmd = cn.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.AddWithValue("$firma", firma);
        cmd.Parameters.AddWithValue("$sprache", language);
        cmd.Parameters.AddWithValue("$artikel", (object?)(articleNumber?.Trim() ?? string.Empty));

        var obj = await cmd.ExecuteScalarAsync(ct);
        return obj == null || obj is DBNull ? null : Convert.ToString(obj);
    }

    // ---------------- core lookups ----------------

    public async Task<string?> GetPartSpecAsync(int firma, string articleNumber, CancellationToken ct = default)
    {
        const string sql = @"
        SELECT xPartSpec
        FROM S_Artikel
        WHERE Firma = $firma
          AND (
               Artikel = $artikel
            OR Artikel = TRIM($artikel)
            OR CAST(Artikel AS TEXT) = $artikel
            OR CAST(Artikel AS TEXT) = TRIM($artikel)
            OR (CAST($artikel AS INTEGER) IS NOT NULL AND Artikel = CAST($artikel AS INTEGER))
          )
        LIMIT 1;";

        await using var cn = Open();
        await using var cmd = cn.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.AddWithValue("$firma", firma);
        cmd.Parameters.AddWithValue("$artikel", articleNumber?.Trim() ?? string.Empty);

        var obj = await cmd.ExecuteScalarAsync(ct);
        return obj is null || obj is DBNull ? null : Convert.ToString(obj);
    }

    /// <summary>
    /// Returns rows for a given template (e.g., "Wed-Spec1", "Wed-Spec2", "Wed-Marking").
    /// If XS_PartSpecValues exists, joins it and reads ValueText.
    /// Otherwise, uses Column_ID as the payload (mock compact schema).
    /// </summary>
    public async Task<IReadOnlyList<(string XRow, string Payload)>> GetRowsAsync(
        int firma, string partSpec, string template, CancellationToken ct)
    {
        // detect schema once
        if (_hasValuesTable is null)
            _hasValuesTable = await HasTableAsync("XS_PartSpecValues", ct);

        var list = new List<(string, string)>();

        await using var cn = Open();
        await using var cmd = cn.CreateCommand();

        if (_hasValuesTable == true)
        {
            cmd.CommandText = @"
SELECT s.xRow, v.ValueText
FROM XS_PartSpecSpecs s
JOIN XS_PartSpecValues v
  ON v.Firma = s.Firma AND v.PartSpec = s.PartSpec AND v.Column_ID = s.Column_ID
WHERE s.Firma = $firma AND s.PartSpec = $ps AND s.Template = $tpl;";
        }
        else
        {
            // Fallback: payload is stored directly in Column_ID
            cmd.CommandText = @"
SELECT s.xRow, s.Column_ID AS ValueText
FROM XS_PartSpecSpecs s
WHERE s.Firma = $firma AND s.PartSpec = $ps AND s.Template = $tpl;";
        }

        cmd.Parameters.AddWithValue("$firma", firma);
        cmd.Parameters.AddWithValue("$ps", partSpec);
        cmd.Parameters.AddWithValue("$tpl", template);

        await using var rdr = await cmd.ExecuteReaderAsync(CommandBehavior.CloseConnection, ct);
        while (await rdr.ReadAsync(ct))
        {
            var xrow = rdr.IsDBNull(0) ? string.Empty : rdr.GetString(0);
            var payload = rdr.IsDBNull(1) ? string.Empty : rdr.GetString(1);
            if (!string.IsNullOrWhiteSpace(xrow))
                list.Add((xrow, payload));
        }

        _log.LogDebug("Loaded {Count} rows for template {Template} (PS={PartSpec}) via {Path}",
            list.Count, template, partSpec, _hasValuesTable == true ? "ValuesTable" : "SpecsOnly");
        return list;
    }

    // ---------------- diagnostics helpers ----------------

    public async Task<IReadOnlyList<string>> ListTablesAsync(CancellationToken ct)
    {
        const string sql = @"SELECT name FROM sqlite_master WHERE type='table' ORDER BY name;";
        var list = new List<string>();
        await using var cn = Open();
        await using var cmd = cn.CreateCommand();
        cmd.CommandText = sql;
        await using var rdr = await cmd.ExecuteReaderAsync(ct);
        while (await rdr.ReadAsync(ct))
        {
            if (!rdr.IsDBNull(0)) list.Add(rdr.GetString(0));
        }
        return list;
    }

    public async Task<IReadOnlyList<(int Cid, string Name, string Type)>> GetTableInfoAsync(string table, CancellationToken ct)
    {
        var list = new List<(int, string, string)>();
        await using var cn = Open();
        await using var cmd = cn.CreateCommand();
        cmd.CommandText = $"PRAGMA table_info({table});";
        await using var rdr = await cmd.ExecuteReaderAsync(ct);
        while (await rdr.ReadAsync(ct))
        {
            var cid = rdr.IsDBNull(0) ? -1 : rdr.GetInt32(0);
            var name = rdr.IsDBNull(1) ? "" : rdr.GetString(1);
            var type = rdr.IsDBNull(2) ? "" : rdr.GetString(2);
            list.Add((cid, name, type));
        }
        return list;
    }

    public async Task<IReadOnlyList<(string Artikel, string? PartSpec)>> GetFirstArticlesAsync(int firma, int limit, CancellationToken ct)
    {
        var list = new List<(string, string?)>();
        await using var cn = Open();
        await using var cmd = cn.CreateCommand();
        cmd.CommandText = @"SELECT Artikel, xPartSpec FROM S_Artikel WHERE Firma = $firma LIMIT $limit;";
        cmd.Parameters.AddWithValue("$firma", firma);
        cmd.Parameters.AddWithValue("$limit", limit);
        await using var rdr = await cmd.ExecuteReaderAsync(ct);
        while (await rdr.ReadAsync(ct))
        {
            var art = rdr.IsDBNull(0) ? "" : rdr.GetString(0);
            var ps = rdr.IsDBNull(1) ? null : rdr.GetString(1);
            list.Add((art, ps));
        }
        return list;
    }

    private async Task<bool> HasTableAsync(string tableName, CancellationToken ct)
    {
        await using var cn = Open();
        await using var cmd = cn.CreateCommand();
        cmd.CommandText = @"SELECT 1 FROM sqlite_master WHERE type='table' AND name=$name;";
        cmd.Parameters.AddWithValue("$name", tableName);
        var obj = await cmd.ExecuteScalarAsync(ct);
        return obj != null;
    }
}

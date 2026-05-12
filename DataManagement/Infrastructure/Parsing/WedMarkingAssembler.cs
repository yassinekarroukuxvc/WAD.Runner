using WAD.Runner.DataManagement.Domain.Wedge;

namespace WAD.Runner.DataManagement.Infrastructure.Parsing;

public static class WedMarkingAssembler
{

    public const string OverlayKey = "Marking-Overlay";
    public const string TextKey = "Marking-Text";

    public static readonly string[] TbKeys = new[]
    {
        "Marking-TB-1","Marking-TB-2","Marking-TB-3","Marking-TB-4",
        "Marking-TB-5","Marking-TB-6","Marking-TB-7"
    };

    public static WedMarking FromRows(IEnumerable<(string XRow, string? Text)> rows)
    {
        if (rows is null) throw new ArgumentNullException(nameof(rows));

        var dict = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (var (xrow, text) in rows)
        {
            if (string.IsNullOrWhiteSpace(xrow)) continue;

            if (!dict.ContainsKey(xrow) || string.IsNullOrWhiteSpace(dict[xrow]))
                dict[xrow] = Normalize(text);
        }

        string? overlay = Get(dict, OverlayKey);
        var tb1 = Get(dict, TbKeys[0]);
        var tb2 = Get(dict, TbKeys[1]);
        var tb3 = Get(dict, TbKeys[2]);
        var tb4 = Get(dict, TbKeys[3]);
        var tb5 = Get(dict, TbKeys[4]);
        var tb6 = Get(dict, TbKeys[5]);
        var tb7 = Get(dict, TbKeys[6]);
        string? textNote = Get(dict, TextKey);

        return new WedMarking(
            overlay,
            tb1, tb2, tb3, tb4, tb5, tb6, tb7,
            textNote
        );
    }

    private static string? Get(IDictionary<string, string?> dict, string key)
        => dict.TryGetValue(key, out var v) && !string.IsNullOrWhiteSpace(v) ? v : null;

    private static string? Normalize(string? s)
        => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}

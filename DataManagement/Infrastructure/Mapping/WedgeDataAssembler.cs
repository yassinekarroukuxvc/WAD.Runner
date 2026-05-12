using WAD.Runner.DataManagement.Domain.Dimensions;
using WAD.Runner.DataManagement.Domain.Wedge;
using WAD.Runner.DataManagement.Infrastructure.Parsing;
using WAD.Runner.DataManagement.Infrastructure.Transport.Dtos;

namespace WAD.Runner.DataManagement.Infrastructure.Mapping;

public static class WedgeDataAssembler
{

    public static WedgeData BuildForWed(
        WedSpec1Dto spec1,
        IEnumerable<WedSpec2RowDto> spec2Rows,
        WedKValueDto? kvalue,
        IEnumerable<WedMarkingRowDto> markingRows)
    {
        if (spec1 is null) throw new ArgumentNullException(nameof(spec1));
        if (spec2Rows is null) throw new ArgumentNullException(nameof(spec2Rows));
        if (markingRows is null) throw new ArgumentNullException(nameof(markingRows));

        var dims = AssembleDimensions(spec2Rows.Select(r => (r.Key, r.Payload)));

        KValue? kv = null;
        if (kvalue is not null && !string.IsNullOrWhiteSpace(kvalue.Payload))
        {
            var (kMm, cmt) = KValueParser.Parse(kvalue.Payload);
            kv = new KValue(kMm, cmt);
        }

        var marking = WedMarkingAssembler.FromRows(markingRows.Select(r => (r.XRow, r.Text)));

        var props = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            ["Wed-Polish"] = NullIfWhite(spec1.WedPolish),
            ["Wed-PS"] = NullIfWhite(spec1.WedPS),
            ["Wed-Notes"] = NullIfWhite(spec1.WedNotes),
            ["Wed-Overlay"] = NullIfWhite(spec1.WedOverlay),

            ["Wed-Engrave"] = NullIfWhite(spec1.WedEngrave),
            ["Wed-Coining"] = NullIfWhite(spec1.WedCoining),

            ["Wed-Dwg-Text1"] = NullIfWhite(spec1.DwgText1),
            ["Wed-Dwg-Text2"] = NullIfWhite(spec1.DwgText2),
            ["Wed-Dwg-Text3"] = NullIfWhite(spec1.DwgText3),
            ["Wed-Dwg-Text4"] = NullIfWhite(spec1.DwgText4),
            ["Wed-Dwg-Text5"] = NullIfWhite(spec1.DwgText5),
            ["Wed-Dwg-Text6"] = NullIfWhite(spec1.DwgText6),
            ["Wed-Dwg-Text7"] = NullIfWhite(spec1.DwgText7),

            ["Wed-Type"] = NullIfWhite(spec1.WedType),
            ["Wed-Foot_Option"] = NullIfWhite(spec1.WedFootOption),
            ["Wed-Wire_Exit"] = NullIfWhite(spec1.WedWireExit),
            ["Wed-Feed_H/Slot"] = NullIfWhite(spec1.WedFeedHSlot),
            ["Wed-FG-Style"] = NullIfWhite(spec1.WedFgStyle),
        };
        var rawStyle = spec1.WedFgStyle;
        var sanitizedStyle = WedgeStyleParser.SanitizeRaw(rawStyle);

        if (!string.IsNullOrWhiteSpace(sanitizedStyle))
        {
            props["wedge_style"] = sanitizedStyle;
        }

        if (WedgeStyleParser.TryParseWedgeType(rawStyle, out var parsedWedgeType))
        {
            props["wedge_type"] = parsedWedgeType.ToString();
        }

        return new WedgeData(
            articleNumber: spec1.ArticleNumber,
            subclass: WedgeSubclass.FG,
            dimensions: dims,
            kValue: kv,
            marking: marking,
            properties: props
        );
    }

    public static WedgeData BuildForPgb(
        PgbSpec1Dto spec1,
        IEnumerable<PgbSpec2RowDto> spec2Rows)
    {
        if (spec1 is null) throw new ArgumentNullException(nameof(spec1));
        if (spec2Rows is null) throw new ArgumentNullException(nameof(spec2Rows));

        var dims = AssembleDimensions(spec2Rows.Select(r => (r.Key, r.Payload)));

        var props = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            ["PGB-Polish"] = NullIfWhite(spec1.Polish),
            ["PGB-PS"] = NullIfWhite(spec1.PS),
            ["PGB-Remarks"] = NullIfWhite(spec1.Remarks),

            ["Wed-Engrave"] = NullIfWhite(spec1.Engrave),
            ["Wed-Coining"] = NullIfWhite(spec1.FLBlank),

            ["Wed-Dwg-Text1"] = NullIfWhite(spec1.DwgText1),
            ["Wed-Dwg-Text2"] = NullIfWhite(spec1.DwgText2),
            ["Wed-Dwg-Text3"] = NullIfWhite(spec1.DwgText3),
            ["Wed-Dwg-Text4"] = NullIfWhite(spec1.DwgText4),
            ["Wed-Dwg-Text5"] = NullIfWhite(spec1.DwgText5),
            ["Wed-Dwg-Text6"] = NullIfWhite(spec1.DwgText6),
            ["Wed-Dwg-Text7"] = NullIfWhite(spec1.DwgText7),

            ["Wed-Type"] = NullIfWhite(spec1.WedType),
            ["Wed-Foot_Option"] = NullIfWhite(spec1.WedFootOption),
            ["Wed-Wire_Exit"] = NullIfWhite(spec1.WedWireExit),
            ["Wed-Feed_H/Slot"] = NullIfWhite(spec1.WedFeedHSlot),
            ["PGB-FG-Style"] = NullIfWhite(spec1.PgbFgStyle),
        };
        var rawStyle = spec1.PgbFgStyle;
        var sanitizedStyle = WedgeStyleParser.SanitizeRaw(rawStyle);

        if (!string.IsNullOrWhiteSpace(sanitizedStyle))
        {
            props["wedge_style"] = sanitizedStyle;
        }

        if (WedgeStyleParser.TryParseWedgeType(rawStyle, out var parsedWedgeType))
        {
            props["wedge_type"] = parsedWedgeType.ToString();
        }

        return new WedgeData(
            articleNumber: spec1.ArticleNumber,
            subclass: WedgeSubclass.PGB,
            dimensions: dims,
            kValue: null,
            marking: null,
            properties: props
        );
    }

    private static IReadOnlyDictionary<DimensionKey, Dimension> AssembleDimensions(
        IEnumerable<(string TransportKey, string Payload)> rows)
    {
        var dict = new Dictionary<DimensionKey, Dimension>();

        foreach (var (tkey, payload) in rows)
        {
            if (string.IsNullOrWhiteSpace(tkey)) continue;

            var key = DimensionKeyPolicy.ToDomainKey(tkey);
            if (key.IsEmpty) continue;

            if (DimensionKeyPolicy.IsAngle(tkey))
            {
                var (deg, tolZero, comment) = DimensionPayloadParser.ParseAngleRow(payload);
                dict[key] = Dimension.CreateAngle(key, deg, comment);
            }
            else
            {
                var (mm, tol, comment) = DimensionPayloadParser.ParseLengthRow(payload);
                dict[key] = Dimension.CreateLength(key, mm, tol, comment);
            }
        }

        return dict;
    }

    private static string? NullIfWhite(string? s)
        => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}

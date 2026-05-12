using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using WAD.Runner.Application.Ports;
using WAD.Runner.DataManagement.Domain.Wedge;
using WAD.Runner.DataManagement.Infrastructure.Mapping;
using WAD.Runner.DataManagement.Infrastructure.Parsing;
using WAD.Runner.DataManagement.Infrastructure.Sqlite;
using WAD.Runner.DataManagement.Infrastructure.Transport.Dtos;

namespace WAD.Runner.DataManagement.Infrastructure.Adapters;

public sealed class SqliteWedgeDataSource : IWedgeDataSource
{
    private readonly ProAlphaRepository _repo;
    private readonly int _firma;
    private readonly string _language;
    private readonly ILogger<SqliteWedgeDataSource> _log;

    public SqliteWedgeDataSource(ProAlphaRepository repo, int firma, ILogger<SqliteWedgeDataSource>? log = null)
        : this(repo, firma, language: "E", log)
    {
    }

    public SqliteWedgeDataSource(ProAlphaRepository repo, int firma, string language, ILogger<SqliteWedgeDataSource>? log = null)
    {
        _repo = repo ?? throw new ArgumentNullException(nameof(repo));
        _firma = firma;
        _language = string.IsNullOrWhiteSpace(language) ? "E" : language.Trim();
        _log = log ?? NullLogger<SqliteWedgeDataSource>.Instance;
    }

    public async Task<WedgeData> LoadAsync(string articleNumber, WedgeSubclass subclass, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(articleNumber))
            throw new ArgumentException("Article number is required.", nameof(articleNumber));

        var partSpec = await _repo.GetPartSpecAsync(_firma, articleNumber, ct)
                       ?? throw new InvalidOperationException($"Article {articleNumber} has no PartSpec.");

        WedgeData baseWedge = subclass switch
        {
            WedgeSubclass.FG => await LoadWedAsync(articleNumber, partSpec, ct),
            WedgeSubclass.PGB => await LoadPgbAsync(articleNumber, partSpec, ct),
            _ => throw new NotSupportedException($"Subclass {subclass} is not supported.")
        };

        try
        {
            var rawDescription = await _repo.GetArticleDescriptionAsync(_firma, articleNumber, _language, ct);
            var description = ArticleDescriptionParser.NormalizeForDisplay(rawDescription);

            if (!string.IsNullOrWhiteSpace(description))
            {
                baseWedge = WithProperty(baseWedge, "article_description", description);
            }
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "GetArticleDescriptionAsync failed for Article={Article}, Lang={Lang}", articleNumber, _language);
        }

        return baseWedge;
    }

    private async Task<WedgeData> LoadWedAsync(string articleNumber, string partSpec, CancellationToken ct)
    {

        const string TplSpec1 = "Wed-Spec1";
        const string TplSpec2 = "Wed-Spec2";
        const string TplMarking = "Wed-Marking";

        var spec1Rows = await _repo.GetRowsAsync(_firma, partSpec, TplSpec1, ct);
        var spec2Rows = await _repo.GetRowsAsync(_firma, partSpec, TplSpec2, ct);
        var markingRows = await _repo.GetRowsAsync(_firma, partSpec, TplMarking, ct);

        var spec1Dto = new WedSpec1Dto(
            ArticleNumber: articleNumber,
            WedPolish: Get(spec1Rows, "Wed-Polish"),
            WedPS: Get(spec1Rows, "Wed-PS"),
            WedNotes: Get(spec1Rows, "Wed-Notes"),
            WedOverlay: Get(spec1Rows, "Wed-Overlay"),

            WedEngrave: Get(spec1Rows, "Wed-Engrave"),
            WedCoining: Get(spec1Rows, "Wed-Coining"),
            WedFgStyle: Get(spec1Rows, "Wed-FG-Style"),

            WedType: Get(spec1Rows, "Wed-Type"),
            WedFootOption: Get(spec1Rows, "Wed-Foot_Option"),
            WedWireExit: Get(spec1Rows, "Wed-Wire_Exit"),
            WedFeedHSlot: Get(spec1Rows, "Wed-Feed_H/Slot"),

            DwgText1: Get(spec1Rows, "Wed-Dwg-Text1"),
            DwgText2: Get(spec1Rows, "Wed-Dwg-Text2"),
            DwgText3: Get(spec1Rows, "Wed-Dwg-Text3"),
            DwgText4: Get(spec1Rows, "Wed-Dwg-Text4"),
            DwgText5: Get(spec1Rows, "Wed-Dwg-Text5"),
            DwgText6: Get(spec1Rows, "Wed-Dwg-Text6"),
            DwgText7: Get(spec1Rows, "Wed-Dwg-Text7")
        );

        var spec2Dto = spec2Rows
            .Where(r => !string.Equals(r.XRow, "Wed_K-Value", StringComparison.OrdinalIgnoreCase))
            .Select(r => new WedSpec2RowDto(Key: r.XRow, Payload: r.Payload))
            .ToList();

        WedKValueDto? kDto = null;
        var kRaw = Get(spec2Rows, "Wed_K-Value");
        if (!string.IsNullOrWhiteSpace(kRaw))
        {

            kDto = new WedKValueDto(kRaw);
            if (!KValueParser.TryParse(kRaw, out _, out _))
                _log.LogWarning("Wed_K-Value payload looks invalid: {Payload}", kRaw);
        }

        var markingDtos = markingRows
            .Select(r => new WedMarkingRowDto(XRow: r.XRow, Text: string.IsNullOrWhiteSpace(r.Payload) ? null : r.Payload))
            .ToList();

        return WedgeDataAssembler.BuildForWed(spec1Dto, spec2Dto, kDto, markingDtos);
    }

    private async Task<WedgeData> LoadPgbAsync(string articleNumber, string partSpec, CancellationToken ct)
    {

        const string TplSpec1 = "PGB-Spec1";
        const string TplSpec2 = "PGB-Spec2";

        var spec1Rows = await _repo.GetRowsAsync(_firma, partSpec, TplSpec1, ct);
        var spec2Rows = await _repo.GetRowsAsync(_firma, partSpec, TplSpec2, ct);

        var spec1Dto = new PgbSpec1Dto(
            ArticleNumber: articleNumber,
            Polish: Get(spec1Rows, "PGB-Polish"),
            PS: Get(spec1Rows, "PGB-PS"),
            Remarks: Get(spec1Rows, "PGB-Remarks"),

            Engrave: Get(spec1Rows, "Wed-Engrave"),
            FLBlank: Get(spec1Rows, "Wed-FL-Blank"),
            PgbFgStyle: Get(spec1Rows, "PGB-FG-Style"),
            DwgText1: Get(spec1Rows, "Wed-Dwg-Text1"),
            DwgText2: Get(spec1Rows, "Wed-Dwg-Text2"),
            DwgText3: Get(spec1Rows, "Wed-Dwg-Text3"),
            DwgText4: Get(spec1Rows, "Wed-Dwg-Text4"),
            DwgText5: Get(spec1Rows, "Wed-Dwg-Text5"),
            DwgText6: Get(spec1Rows, "Wed-Dwg-Text6"),
            DwgText7: Get(spec1Rows, "Wed-Dwg-Text7"),

            WedType: Get(spec1Rows, "Wed-Type"),
            WedFootOption: Get(spec1Rows, "Wed-Foot_Option"),
            WedWireExit: Get(spec1Rows, "Wed-Wire_Exit"),
            WedFeedHSlot: Get(spec1Rows, "Wed-Feed_H/Slot")
        );

        var spec2Dto = spec2Rows
            .Select(r => new PgbSpec2RowDto(Key: r.XRow, Payload: r.Payload))
            .ToList();

        return WedgeDataAssembler.BuildForPgb(spec1Dto, spec2Dto);
    }

    private static string? Get(IEnumerable<(string XRow, string Payload)> rows, string key)
        => rows.FirstOrDefault(r => string.Equals(r.XRow, key, StringComparison.OrdinalIgnoreCase)).Payload;

    private static WedgeData WithProperty(WedgeData w, string key, string? value)
    {
        var props = new Dictionary<string, string?>(w.Properties, StringComparer.OrdinalIgnoreCase)
        {
            [key] = value
        };

        return new WedgeData(
            articleNumber: w.ArticleNumber,
            subclass: w.Subclass,
            dimensions: w.Dimensions,
            kValue: w.KValue,
            marking: w.Marking,
            properties: props
        );
    }
}

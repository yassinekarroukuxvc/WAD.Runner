// DataManagement/Infrastructure/Adapters/JavaWedgeDataSource.cs
using System;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.Extensions.Logging;

using WAD.Runner.Application.Ports;
using WAD.Runner.DataManagement.Domain.Wedge;
using WAD.Runner.DataManagement.Infrastructure.Mapping;
using WAD.Runner.DataManagement.Infrastructure.Parsing;

namespace WAD.Runner.DataManagement.Infrastructure.Adapters;

/// <summary>
/// IWedgeDataSource implementation backed by the Java API transport.
/// - Fetches Spec1/Spec2 (+ KValue/Marking for FG)
/// - Maps transport DTOs → domain WedgeData via WedgeDataAssembler
/// - Adds light validation and logging
/// </summary>
public sealed class JavaWedgeDataSource : IWedgeDataSource
{
    private readonly IJavaWedgeTransport _api;
    private readonly ILogger<JavaWedgeDataSource>? _log;

    public JavaWedgeDataSource(IJavaWedgeTransport api, ILogger<JavaWedgeDataSource>? log = null)
    {
        _api = api ?? throw new ArgumentNullException(nameof(api));
        _log = log;
    }

    public async Task<WedgeData> LoadAsync(string articleNumber, WedgeSubclass subclass, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(articleNumber))
            throw new ArgumentException("Article number is required.", nameof(articleNumber));

        _log?.LogDebug("Loading wedge data. Article={Article} Subclass={Subclass}", articleNumber, subclass);

        switch (subclass)
        {
            // ---------- FG (Wed-*) ----------
            case WedgeSubclass.FG:
                {
                    var spec1 = await _api.GetWedSpec1Async(articleNumber, ct);
                    var spec2 = await _api.GetWedSpec2Async(articleNumber, ct);
                    var kvalue = await _api.GetWedKValueAsync(articleNumber, ct);
                    var marking = await _api.GetWedMarkingAsync(articleNumber, ct);
                    var rawDescription = await _api.GetArticleDescriptionAsync(articleNumber, ct);
                    var description = ArticleDescriptionParser.NormalizeForDisplay(rawDescription);

                    if (spec2 is null || spec2.Count == 0)
                        throw new InvalidOperationException($"No Wed-Spec2 rows returned for article {articleNumber}.");

                    var wd = WedgeDataAssembler.BuildForWed(spec1, spec2, kvalue, marking);

                    if (!string.IsNullOrWhiteSpace(description))
                    {
                        var props = new Dictionary<string, string?>(wd.Properties, StringComparer.OrdinalIgnoreCase)
                        {
                            ["article_description"] = description
                        };

                        wd = new WedgeData(
                            wd.ArticleNumber,
                            wd.Subclass,
                            wd.Dimensions,
                            wd.KValue,
                            wd.Marking,
                            props);
                    }

                    _log?.LogInformation("Loaded FG wedge data: {Article} with {DimCount} dims", wd.ArticleNumber, wd.Dimensions.Count);
                    return wd;
                }

            // ---------- PGB ----------
            case WedgeSubclass.PGB:
                {
                    var spec1 = await _api.GetPgbSpec1Async(articleNumber, ct);
                    var spec2 = await _api.GetPgbSpec2Async(articleNumber, ct);
                    var rawDescription = await _api.GetArticleDescriptionAsync(articleNumber, ct);
                    var description = ArticleDescriptionParser.NormalizeForDisplay(rawDescription);

                    if (spec2 is null || spec2.Count == 0)
                        throw new InvalidOperationException($"No PGB-Spec2 rows returned for article {articleNumber}.");

                    var wd = WedgeDataAssembler.BuildForPgb(spec1, spec2);

                    if (!string.IsNullOrWhiteSpace(description))
                    {
                        var props = new Dictionary<string, string?>(wd.Properties, StringComparer.OrdinalIgnoreCase)
                        {
                            ["article_description"] = description
                        };

                        wd = new WedgeData(
                            wd.ArticleNumber,
                            wd.Subclass,
                            wd.Dimensions,
                            wd.KValue,
                            wd.Marking,
                            props);
                    }

                    _log?.LogInformation("Loaded PGB wedge data: {Article} with {DimCount} dims", wd.ArticleNumber, wd.Dimensions.Count);
                    return wd;
                }
            default:
                throw new NotSupportedException($"Subclass '{subclass}' is not supported.");
        }
    }
}

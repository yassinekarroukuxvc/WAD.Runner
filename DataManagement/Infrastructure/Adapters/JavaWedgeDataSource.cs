// DataManagement/Infrastructure/Adapters/JavaWedgeDataSource.cs
using System;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.Extensions.Logging;

using WAD.Runner.Application.Ports;
using WAD.Runner.DataManagement.Domain.Wedge;
using WAD.Runner.DataManagement.Infrastructure.Mapping;

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

                    if (spec2 is null || spec2.Count == 0)
                        throw new InvalidOperationException($"No Wed-Spec2 rows returned for article {articleNumber}.");

                    // NOTE:
                    // The client mappings (Wed-Engrave, Wed-Dwg-Text1..7, Wed-Coining) are handled in the
                    // DataManagement mapping layer (WedgeDataAssembler) PROVIDED the transport returns them
                    // as part of Wed-Spec1.
                    //
                    // If the Java API already returns those fields in spec1, no extra work is needed here.
                    // If not, the transport DTO must be extended to include them.

                    var wd = WedgeDataAssembler.BuildForWed(spec1, spec2, kvalue, marking);
                    _log?.LogInformation("Loaded FG wedge data: {Article} with {DimCount} dims", wd.ArticleNumber, wd.Dimensions.Count);
                    return wd;
                }

            // ---------- PGB ----------
            case WedgeSubclass.PGB:
                {
                    var spec1 = await _api.GetPgbSpec1Async(articleNumber, ct);
                    var spec2 = await _api.GetPgbSpec2Async(articleNumber, ct);

                    if (spec2 is null || spec2.Count == 0)
                        throw new InvalidOperationException($"No PGB-Spec2 rows returned for article {articleNumber}.");

                    // NOTE:
                    // The client mappings (Wed-Engrave, Wed-Dwg-Text1..7, Wed-FL-Blank) should be returned
                    // by the Java API as part of PGB-Spec1. As long as the transport DTO is updated to
                    // include these fields, WedgeDataAssembler will normalize them into WedgeData.Properties.

                    var wd = WedgeDataAssembler.BuildForPgb(spec1, spec2);
                    _log?.LogInformation("Loaded PGB wedge data: {Article} with {DimCount} dims", wd.ArticleNumber, wd.Dimensions.Count);
                    return wd;
                }

            default:
                throw new NotSupportedException($"Subclass '{subclass}' is not supported.");
        }
    }
}

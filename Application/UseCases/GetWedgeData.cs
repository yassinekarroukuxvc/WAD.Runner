using WAD.Runner.Application.Ports;
using WAD.Runner.DataManagement.Domain.Wedge;

namespace WAD.Runner.Application.UseCases;

/// <summary>
/// Loads normalized WedgeData (FG/PGB) from the configured data source
/// (SQLite or Java transport, depending on DI setup).
/// </summary>
public sealed class GetWedgeData
{
    private readonly IWedgeDataSource _source;

    public GetWedgeData(IWedgeDataSource source) => _source = source;

    public Task<WedgeData> ExecuteAsync(string articleNumber, WedgeSubclass subclass, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(articleNumber))
            throw new ArgumentException("Article number is required.", nameof(articleNumber));

        return _source.LoadAsync(articleNumber.Trim(), subclass, ct);
    }
}

using WAD.Runner.DataManagement.Domain.Wedge;

namespace WAD.Runner.Application.Ports;

/// <summary>
/// High-level source for normalized wedge data (FG/PGB), regardless of origin
/// (Java API, SQLite mock, etc.). Returns unit-normalized domain aggregates.
/// </summary>
public interface IWedgeDataSource
{
    /// <summary>
    /// Load wedge data for an article and subclass. Implementation may call
    /// external services/DBs and should map into domain models.
    /// </summary>
    Task<WedgeData> LoadAsync(string articleNumber, WedgeSubclass subclass, CancellationToken ct);
}

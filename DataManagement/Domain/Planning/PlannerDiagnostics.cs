namespace WAD.Runner.DataManagement.Domain.Planning;

/// <summary>Diagnostic record for planning warnings.</summary>
public sealed record PlanningWarning(string Code, string Message, string? Key = null, string? View = null);

/// <summary>Collector for planning-time warnings (missing dims, views, etc.).</summary>
public sealed class PlannerDiagnostics
{
    private readonly List<PlanningWarning> _warnings = new();
    public IReadOnlyList<PlanningWarning> Warnings => _warnings;

    public void MissingDimension(string key) =>
        _warnings.Add(new PlanningWarning("PLN001", $"Missing dimension '{key}'.", key));

    public void MissingView(string view) =>
        _warnings.Add(new PlanningWarning("PLN002", $"Missing view config '{view}'.", null, view));

    public void Suspicious(string code, string message, string? key = null, string? view = null) =>
        _warnings.Add(new PlanningWarning(code, message, key, view));

    public bool HasWarnings => _warnings.Count > 0;
}

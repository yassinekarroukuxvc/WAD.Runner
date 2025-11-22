namespace WAD.Runner.DataManagement.Domain.Wedge;

/// <summary>
/// Marking data from Wed-Marking xRows: Overlay, TB-1..7, and free Text.
/// </summary>
public sealed record WedMarking(
    string? Overlay,
    string? TB1, string? TB2, string? TB3, string? TB4, string? TB5, string? TB6, string? TB7,
    string? Text
)
{
    public static WedMarking Empty => new(null, null, null, null, null, null, null, null, null);
}

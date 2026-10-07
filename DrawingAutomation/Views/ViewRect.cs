using System;

namespace WAD.Runner.DrawingAutomation.Views;

/// <summary>
/// Axis-aligned rectangle in sheet coordinates (meters).
/// </summary>
public sealed class ViewRect
{
    private const double Epsilon = 1e-9;

    public ViewRect(
        double minX,
        double minY,
        double maxX,
        double maxY)
    {
        MinX = Math.Min(minX, maxX);
        MaxX = Math.Max(minX, maxX);
        MinY = Math.Min(minY, maxY);
        MaxY = Math.Max(minY, maxY);
    }

    public double MinX { get; }
    public double MinY { get; }
    public double MaxX { get; }
    public double MaxY { get; }

    public double Width => MaxX - MinX;
    public double Height => MaxY - MinY;

    public ViewRect Inflate(
        double amount)
        => new ViewRect(
            MinX - amount,
            MinY - amount,
            MaxX + amount,
            MaxY + amount);

    public bool Overlaps(
        ViewRect other)
    {
        if (other is null)
            throw new ArgumentNullException(nameof(other));

        return
            MinX < other.MaxX - Epsilon
            && MaxX > other.MinX + Epsilon
            && MinY < other.MaxY - Epsilon
            && MaxY > other.MinY + Epsilon;
    }

    public bool IsInside(
        ViewRect outer)
    {
        if (outer is null)
            throw new ArgumentNullException(nameof(outer));

        return
            MinX >= outer.MinX - Epsilon
            && MaxX <= outer.MaxX + Epsilon
            && MinY >= outer.MinY - Epsilon
            && MaxY <= outer.MaxY + Epsilon;
    }

    public override string ToString()
        => $"({MinX:F4}, {MinY:F4})-({MaxX:F4}, {MaxY:F4})";
}
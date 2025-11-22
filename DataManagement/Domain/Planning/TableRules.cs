using WAD.Runner.DataManagement.Domain.Drawing;

namespace WAD.Runner.DataManagement.Domain.Planning;

internal static class TableRules
{
    public static List<TableSpec> Build(DrawingData drawing)
    {
        var list = new List<TableSpec>();

        foreach (var (key, t) in drawing.Tables)
        {
            list.Add(new TableSpec
            {
                Id = key,
                PositionMm = t.PositionMm,
                SizeMm = t.SizeMm
            });
        }

        return list;
    }
}

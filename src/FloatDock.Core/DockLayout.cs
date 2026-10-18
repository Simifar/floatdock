namespace FloatDock.Core;

/// <summary>Layout maths of the magnified dock; no UI types, so it is tested directly.</summary>
public static class DockLayout
{
    /// <summary>
    /// The scale of an icon whose natural center is <paramref name="centerX"/> while the pointer is at <paramref name="pointerX"/>:
    /// a bell curve from 1 (far away) to <paramref name="magnification"/> (under the pointer), about 1.8 icons wide.
    /// </summary>
    public static double Scale(double pointerX, double centerX, double iconSize, double magnification)
    {
        var distance = (pointerX - centerX) / Math.Max(1, iconSize * 1.8);
        return 1 + (Math.Max(1, magnification) - 1) * Math.Exp(-distance * distance * 2);
    }

    /// <summary>
    /// Horizontal shifts that make magnified icons push their neighbours aside instead of overlapping them, keeping the
    /// row centered. Items are scaled around their own center, so each one moves by the extra width of everything
    /// to its left plus half of its own, minus half of the extra width of the whole row.
    /// </summary>
    public static double[] Offsets(IReadOnlyList<double> widths, IReadOnlyList<double> scales)
    {
        var count = Math.Min(widths.Count, scales.Count);
        var extra = new double[count];
        for (var i = 0; i < count; i++) extra[i] = widths[i] * (scales[i] - 1);
        var total = extra.Sum();
        var offsets = new double[count];
        var before = 0.0;
        for (var i = 0; i < count; i++)
        {
            offsets[i] = before + extra[i] / 2 - total / 2;
            before += extra[i];
        }
        return offsets;
    }

    /// <summary>Natural left edges of items laid out in a row with a fixed gap.</summary>
    public static double[] Lefts(IReadOnlyList<double> widths, double spacing)
    {
        var lefts = new double[widths.Count];
        var x = 0.0;
        for (var i = 0; i < widths.Count; i++)
        {
            lefts[i] = x;
            x += widths[i] + spacing;
        }
        return lefts;
    }
}

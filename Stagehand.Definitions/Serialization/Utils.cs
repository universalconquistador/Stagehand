using System;
using System.Collections.Generic;
using System.Text;

namespace Stagehand.Definitions.Serialization;

internal class Utils
{
    /// <summary>
    /// Replaces <see cref="Single.PositiveInfinity"/> with <see cref="Single.MaxValue"/>,
    /// <see cref="Single.NegativeInfinity"/> with <see cref="Single.MinValue"/>,
    /// and <see cref="Single.NaN"/> with <c>0.0</c>.
    /// </summary>
    public static float MakeNonInfinite(float value)
    {
        return float.IsNaN(value) ? 0.0f : MathF.Min(MathF.Max(value, float.MinValue), float.MaxValue);
    }
}

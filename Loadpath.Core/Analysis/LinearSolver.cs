namespace Loadpath.Core.Analysis;

/// <summary>Dense Gaussian elimination with partial pivoting. Sizes here are small (2 × nodes).</summary>
public static class LinearSolver
{
    /// <summary>Solves A·x = b in place. Returns false when A is singular (pivot below tolerance).</summary>
    public static bool TrySolve(double[,] a, double[] b, out double[] x, double pivotTolerance = 1e-9)
    {
        var n = b.Length;
        x = new double[n];
        if (n == 0) return true;

        // Scale tolerance to the matrix magnitude so units don't matter.
        double maxAbs = 0;
        for (var i = 0; i < n; i++)
            for (var j = 0; j < n; j++)
                maxAbs = Math.Max(maxAbs, Math.Abs(a[i, j]));
        if (maxAbs == 0) return false;
        var tol = pivotTolerance * maxAbs;

        for (var col = 0; col < n; col++)
        {
            var pivotRow = col;
            var pivotVal = Math.Abs(a[col, col]);
            for (var r = col + 1; r < n; r++)
            {
                var v = Math.Abs(a[r, col]);
                if (v > pivotVal) { pivotVal = v; pivotRow = r; }
            }
            if (pivotVal < tol) return false;

            if (pivotRow != col)
            {
                for (var j = 0; j < n; j++) (a[col, j], a[pivotRow, j]) = (a[pivotRow, j], a[col, j]);
                (b[col], b[pivotRow]) = (b[pivotRow], b[col]);
            }

            var p = a[col, col];
            for (var r = col + 1; r < n; r++)
            {
                var f = a[r, col] / p;
                if (f == 0) continue;
                for (var j = col; j < n; j++) a[r, j] -= f * a[col, j];
                b[r] -= f * b[col];
            }
        }

        for (var i = n - 1; i >= 0; i--)
        {
            var s = b[i];
            for (var j = i + 1; j < n; j++) s -= a[i, j] * x[j];
            x[i] = s / a[i, i];
        }
        return true;
    }
}

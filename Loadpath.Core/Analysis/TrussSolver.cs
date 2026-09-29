using System.Diagnostics;
using Loadpath.Core.Geometry;
using Loadpath.Core.Model;

namespace Loadpath.Core.Analysis;

/// <summary>
/// 2D pin-jointed truss by the direct stiffness method.
/// Units in: meters, kN, mm² (sections), GPa (E). Internally: N, m, Pa.
/// </summary>
public static class TrussSolver
{
    public static AnalysisResult Solve(StructureDocument doc)
    {
        var sw = Stopwatch.StartNew();
        var nodes = doc.Nodes;
        var members = doc.Members;
        if (nodes.Count == 0 && members.Count == 0) return AnalysisResult.EmptyResult;

        var memberResults = new Dictionary<int, MemberResult>(members.Count);
        var nodeResults = new Dictionary<int, NodeResult>(nodes.Count);

        // Geometry-only results are still useful (lengths) even when we cannot solve.
        AnalysisResult Fail(AnalysisStatus status)
        {
            foreach (var m in members)
                memberResults[m.Id] = new MemberResult(m.Id, 0, 0, doc.MemberLength(m), 0, 0);
            foreach (var n in nodes)
                nodeResults[n.Id] = new NodeResult(n.Id, Vec2.Zero, Vec2.Zero);
            return new AnalysisResult(status, memberResults, nodeResults, 0, 0, 0, null, sw.Elapsed.TotalMilliseconds);
        }

        if (!nodes.Any(n => n.HasLoad)) return Fail(AnalysisStatus.NoLoads);

        var fixedDofs = nodes.Sum(n => (n.Support.FixesX() ? 1 : 0) + (n.Support.FixesY() ? 1 : 0));
        if (fixedDofs < 3) return Fail(AnalysisStatus.Unsupported);

        var index = new Dictionary<int, int>(nodes.Count);
        for (var i = 0; i < nodes.Count; i++) index[nodes[i].Id] = i;
        var dofCount = nodes.Count * 2;

        var k = new double[dofCount, dofCount];
        var f = new double[dofCount];

        foreach (var m in members)
        {
            var a = doc.GetNode(m.StartNodeId);
            var b = doc.GetNode(m.EndNodeId);
            var d = b.Position - a.Position;
            var len = d.Length;
            if (len < 1e-9) continue; // zero-length: contributes nothing
            var c = d.X / len;
            var s = d.Y / len;
            var ea = m.Section.ElasticModulusPa * m.Section.AreaM2;
            var kk = ea / len;
            var ia = index[a.Id] * 2;
            var ib = index[b.Id] * 2;
            double[] t = [c, s];
            int[] map = [ia, ia + 1, ib, ib + 1];
            for (var i = 0; i < 4; i++)
            {
                var ti = (i < 2 ? 1 : -1) * t[i % 2];
                for (var j = 0; j < 4; j++)
                {
                    var tj = (j < 2 ? 1 : -1) * t[j % 2];
                    k[map[i], map[j]] += kk * ti * tj;
                }
            }
        }

        foreach (var n in nodes)
        {
            var i = index[n.Id] * 2;
            f[i] += n.Load.X * 1000;
            f[i + 1] += n.Load.Y * 1000;
        }

        // Reduce: keep only free DOFs.
        var free = new List<int>(dofCount);
        foreach (var n in nodes)
        {
            var i = index[n.Id] * 2;
            if (!n.Support.FixesX()) free.Add(i);
            if (!n.Support.FixesY()) free.Add(i + 1);
        }

        var nf = free.Count;
        var kr = new double[nf, nf];
        var fr = new double[nf];
        for (var i = 0; i < nf; i++)
        {
            fr[i] = f[free[i]];
            for (var j = 0; j < nf; j++) kr[i, j] = k[free[i], free[j]];
        }

        // A node with no members and no support has a zero row: mechanism.
        for (var i = 0; i < nf; i++)
        {
            var any = false;
            for (var j = 0; j < nf; j++) if (Math.Abs(kr[i, j]) > 0) { any = true; break; }
            if (!any) return Fail(AnalysisStatus.Mechanism);
        }

        if (!LinearSolver.TrySolve(kr, fr, out var ur)) return Fail(AnalysisStatus.Mechanism);

        var u = new double[dofCount];
        for (var i = 0; i < nf; i++) u[free[i]] = ur[i];

        // Reactions: R = K·u − F over all dofs (nonzero only at fixed ones).
        var maxForce = 0.0;
        var maxUtil = 0.0;
        var maxDisp = 0.0;
        int? critical = null;

        foreach (var m in members)
        {
            var a = doc.GetNode(m.StartNodeId);
            var b = doc.GetNode(m.EndNodeId);
            var d = b.Position - a.Position;
            var len = d.Length;
            if (len < 1e-9)
            {
                memberResults[m.Id] = new MemberResult(m.Id, 0, 0, 0, 0, 0);
                continue;
            }
            var c = d.X / len;
            var s = d.Y / len;
            var ia = index[a.Id] * 2;
            var ib = index[b.Id] * 2;
            var elongation = (u[ib] - u[ia]) * c + (u[ib + 1] - u[ia + 1]) * s;
            var forceN = m.Section.ElasticModulusPa * m.Section.AreaM2 / len * elongation;
            var forceKn = forceN / 1000;
            var stressPa = forceN / m.Section.AreaM2;
            var utilYield = Math.Abs(stressPa) / m.Section.YieldStrengthPa;
            var utilBuckling = forceN < 0 ? -forceN / m.Section.CriticalBucklingLoadN(len) : 0;
            var r = new MemberResult(m.Id, forceKn, stressPa / 1e6, len, utilYield, utilBuckling);
            memberResults[m.Id] = r;
            maxForce = Math.Max(maxForce, Math.Abs(forceKn));
            if (r.Utilization > maxUtil) { maxUtil = r.Utilization; critical = m.Id; }
        }

        foreach (var n in nodes)
        {
            var i = index[n.Id] * 2;
            var disp = new Vec2(u[i], u[i + 1]);
            maxDisp = Math.Max(maxDisp, disp.Length);
            var rx = 0.0;
            var ry = 0.0;
            if (n.Support.FixesX())
            {
                for (var j = 0; j < dofCount; j++) rx += k[i, j] * u[j];
                rx -= f[i];
            }
            if (n.Support.FixesY())
            {
                for (var j = 0; j < dofCount; j++) ry += k[i + 1, j] * u[j];
                ry -= f[i + 1];
            }
            nodeResults[n.Id] = new NodeResult(n.Id, disp, new Vec2(rx / 1000, ry / 1000));
        }

        return new AnalysisResult(AnalysisStatus.Solved, memberResults, nodeResults, maxForce, maxUtil, maxDisp, critical, sw.Elapsed.TotalMilliseconds);
    }
}

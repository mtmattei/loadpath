namespace Loadpath.Core.Model;

/// <summary>Cross-section: area in mm², second moment of area in mm⁴. Sections are shared by members.</summary>
public sealed record Section(string Id, string Name, double AreaMm2, double SecondMomentMm4, Material Material)
{
    /// <summary>Compact label for chips: the size part of the name.</summary>
    public string ShortName => Name.Contains(' ') ? Name[(Name.IndexOf(' ') + 1)..] : Name;

    public double AreaM2 => AreaMm2 * 1e-6;
    public double SecondMomentM4 => SecondMomentMm4 * 1e-12;
    public double ElasticModulusPa => Material.ElasticModulusGPa * 1e9;
    public double YieldStrengthPa => Material.YieldStrengthMPa * 1e6;

    /// <summary>Mass per meter in kg/m.</summary>
    public double MassPerMeter => AreaM2 * Material.DensityKgM3;

    /// <summary>Euler critical buckling load in N for a pin-ended member of the given length in meters.</summary>
    public double CriticalBucklingLoadN(double lengthM) =>
        lengthM <= 1e-9 ? double.PositiveInfinity : Math.PI * Math.PI * ElasticModulusPa * SecondMomentM4 / (lengthM * lengthM);

    public static readonly Section Chs48 = new("chs48", "CHS 48.3×3.2", 453, 116_000, Material.Steel);
    public static readonly Section Chs60 = new("chs60", "CHS 60.3×3.6", 641, 259_000, Material.Steel);
    public static readonly Section Chs76 = new("chs76", "CHS 76.1×4.0", 906, 591_000, Material.Steel);
    public static readonly Section Shs50 = new("shs50", "SHS 50×50×3", 553, 200_000, Material.Steel);
    public static readonly Section Shs80 = new("shs80", "SHS 80×80×4", 1_190, 1_120_000, Material.Steel);
    public static readonly Section Rod16 = new("rod16", "Rod Ø16", 201, 3_217, Material.Steel);
    public static readonly Section Alu50 = new("alu50", "Al tube 50×3", 443, 123_000, Material.Aluminum);
    public static readonly Section Timber45x95 = new("t4595", "Timber 45×95", 4_275, 3_216_000, Material.Timber);

    public static readonly IReadOnlyList<Section> Library = [Rod16, Chs48, Chs60, Chs76, Shs50, Shs80, Alu50, Timber45x95];

    public static Section FindById(string id) => Library.FirstOrDefault(s => s.Id == id) ?? Chs48;
}

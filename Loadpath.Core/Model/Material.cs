namespace Loadpath.Core.Model;

/// <summary>Linear elastic material. E in GPa, yield in MPa, density in kg/m³.</summary>
public sealed record Material(string Name, double ElasticModulusGPa, double YieldStrengthMPa, double DensityKgM3)
{
    public static readonly Material Steel = new("Steel S355", 210, 355, 7850);
    public static readonly Material Aluminum = new("Aluminum 6061-T6", 69, 240, 2700);
    public static readonly Material Timber = new("Timber C24", 11, 24, 420);

    public static readonly IReadOnlyList<Material> Library = [Steel, Aluminum, Timber];

    public static Material FindByName(string name) => Library.FirstOrDefault(m => m.Name == name) ?? Steel;
}

namespace LimbusSplitPro.Models;

/// <summary>
/// Un registro del manifiesto de modelos, según sección 7 del encargo.
/// Cada campo es obligatorio para que un modelo pueda considerarse verificado.
/// </summary>
public sealed record ModelManifestEntry
{
    public required string Id { get; init; }
    public required string RelativePath { get; init; }
    public required string Sha256 { get; init; }
    public required long ExpectedSizeBytes { get; init; }
    public required string Version { get; init; }
    public required string Origin { get; init; }
    public required string CodeLicense { get; init; }
    public required string WeightsLicense { get; init; }
    public required string LicenseEvidenceUrl { get; init; }
    public required string Author { get; init; }
    public required string Attribution { get; init; }
    public required IReadOnlyList<string> Capabilities { get; init; }
    public required bool RedistributionAuthorized { get; init; }
    public required bool CommercialUseAuthorized { get; init; }
    /// <summary>
    /// Si es false, el modelo NUNCA puede incluirse en una build pública,
    /// aunque esté presente físicamente en el árbol de desarrollo.
    /// </summary>
    public bool IsPublicBuildEligible => RedistributionAuthorized && CommercialUseAuthorized;
}

using LimbusSplitPro.Models;
using Xunit;

namespace LimbusSplitPro.Tests;

public class ModelVerifierTests
{
    [Fact]
    public void PublicBuild_RejectsModelWithoutCommercialAuthorization()
    {
        // Reproduce el caso real encontrado en la investigación: htdemucs con licencia
        // de pesos no especificada NUNCA debe pasar la verificación en build pública.
        var entry = new ModelManifestEntry
        {
            Id = "htdemucs",
            RelativePath = "models/demucs/htdemucs",
            Sha256 = new string('0', 64),
            ExpectedSizeBytes = 0,
            Version = "test",
            Origin = "test",
            CodeLicense = "MIT",
            WeightsLicense = "NO_ESPECIFICADA",
            LicenseEvidenceUrl = "https://github.com/facebookresearch/demucs/issues/327",
            Author = "Meta AI",
            Attribution = "test",
            Capabilities = new[] { "voz_principal" },
            RedistributionAuthorized = false,
            CommercialUseAuthorized = false,
        };

        Assert.False(entry.IsPublicBuildEligible);
    }

    // TODO (build real en Windows): tests con archivos de modelo reales, hash correcto/alterado,
    // tamaño incorrecto, y el flujo completo de ModelVerifier.Verify(...) contra disco.
}

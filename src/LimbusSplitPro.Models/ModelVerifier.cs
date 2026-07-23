using System.Security.Cryptography;

namespace LimbusSplitPro.Models;

/// <summary>
/// Verificador fail-closed (sección 7): la build pública debe fallar en el arranque
/// si un modelo no está registrado, tiene el hash alterado, o no tiene autorización
/// clara de redistribución y uso comercial.
/// </summary>
public sealed class ModelVerifier
{
    private readonly bool _isPublicBuild;

    public ModelVerifier(bool isPublicBuild) => _isPublicBuild = isPublicBuild;

    public VerificationResult Verify(ModelManifestEntry entry, string actualFilePath)
    {
        if (!File.Exists(actualFilePath))
            return VerificationResult.Fail(entry.Id, "Archivo de modelo ausente.");

        var actualHash = ComputeSha256(actualFilePath);
        if (!string.Equals(actualHash, entry.Sha256, StringComparison.OrdinalIgnoreCase))
            return VerificationResult.Fail(entry.Id, $"Hash SHA-256 no coincide. Esperado {entry.Sha256}, obtenido {actualHash}.");

        var actualSize = new FileInfo(actualFilePath).Length;
        if (actualSize != entry.ExpectedSizeBytes)
            return VerificationResult.Fail(entry.Id, "Tamaño de archivo distinto al esperado.");

        if (_isPublicBuild && !entry.IsPublicBuildEligible)
            return VerificationResult.Fail(entry.Id,
                "Modelo sin autorización de redistribución/uso comercial verificada: bloqueado en build pública.");

        return VerificationResult.Ok(entry.Id);
    }

    private static string ComputeSha256(string path)
    {
        using var stream = File.OpenRead(path);
        using var sha = SHA256.Create();
        var hash = sha.ComputeHash(stream);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}

public sealed record VerificationResult(string ModelId, bool Success, string? Reason)
{
    public static VerificationResult Ok(string id) => new(id, true, null);
    public static VerificationResult Fail(string id, string reason) => new(id, false, reason);
}

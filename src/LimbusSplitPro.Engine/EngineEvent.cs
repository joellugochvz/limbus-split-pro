namespace LimbusSplitPro.Engine;

/// <summary>
/// Eventos estructurados que el motor Python emite por stdout, uno por línea (JSON Lines).
/// stdout se reserva EXCLUSIVAMENTE para esto; stderr lleva logs técnicos de Python/PyTorch
/// que nunca se muestran directamente al usuario (sección 8 y 18 del encargo).
/// </summary>
public sealed record EngineEvent
{
    public required string Event { get; init; } // "progress" | "stage" | "error" | "result" | "cancelled"
    public string? Stage { get; init; }
    public double? Pct { get; init; }
    public string? ErrorCode { get; init; }      // MODEL_HASH_MISMATCH, OUT_OF_MEMORY, GPU_UNAVAILABLE, ...
    public string? Message { get; init; }
    public IReadOnlyList<string>? OutputFiles { get; init; }
}

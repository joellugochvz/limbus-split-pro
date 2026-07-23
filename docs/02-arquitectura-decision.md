# Limbus Split Pro — Decisión de arquitectura
Fecha: 2026-07-23. Verificado con fuentes oficiales (dotnet.microsoft.com, GitHub dotnet/core, endoflife.date).

## 1. Versiones fijadas (reproducibles)

| Componente | Versión fijada | Motivo | Soporte |
|---|---|---|---|
| .NET | **10.0 (LTS)** | LTS más reciente, GA desde el 11 nov 2025, soportado hasta nov 2028 — cubre sobradamente el ciclo de vida del producto | 3 años |
| Visual Studio (build) | 2026 (v18.0) o `dotnet build`/MSBuild vía CLI en CI, sin dependencia real del IDE | El runtime final no requiere VS instalado | — |
| UI framework | **WPF** (ver justificación abajo) | — | — |
| NAudio | Se fija la última versión estable publicada en NuGet en el momento de crear el `packages.lock.json` — pendiente de congelar el número exacto al generar el primer commit del repo, con hash del paquete registrado | MIT |
| Python embebido | 3.11.x (última patch estable de la rama 3.11, por compatibilidad amplia con PyTorch/ONNX Runtime en Windows x64) | Se fija el `.tar.gz`/`embed.zip` oficial de python.org con SHA-256 en `PROVENANCE.json` | — |
| WiX Toolset (instalador) | v5.x estable | MSI reproducible vía CLI/CI, sin GUI | — |

Todas las versiones exactas (patch number, hash del instalador de Python embebido, hash del paquete NuGet de NAudio) se registran en el primer commit real, no aquí, porque fijarlas sin poder ejecutar `dotnet restore`/descargar el embed de Python en este entorno sería inventar un hash — y la sección 9 del encargo prohíbe explícitamente eso.

## 2. WPF vs WinUI 3 — decisión: **WPF**

| Criterio | WPF | WinUI 3 | Ganador |
|---|---|---|---|
| Madurez de reproducción de audio multipista (NAudio + WASAPI) | Muy alta, décadas de uso en producción, `MixingSampleProvider` probado extensamente | Requiere interop adicional o las mismas libs de audio de .NET vía Win2D/interop; menos ejemplos de mezcla multipista sample-accurate | **WPF** |
| Empaquetado sin MSIX obligatorio | Sí — MSI/EXE tradicional, control total sobre rutas de instalación (`%LOCALAPPDATA%`, Program Files) | WinUI 3 funciona mejor empaquetado como MSIX, lo que complica escribir en rutas fuera del contenedor de la app y complica la exclusión de admin rights en algunos escenarios | **WPF** |
| Compatibilidad Windows 10 (no solo 11) | Total, sin dependencias adicionales del Windows App SDK runtime | Requiere distribuir el Windows App SDK runtime (~decenas de MB adicionales) o exigir su instalación | **WPF** |
| Escalado DPI, accesibilidad, temas claro/oscuro | Soportado y muy documentado, aunque requiere algo más de trabajo manual que Fluent nativo | Fluent Design nativo de fábrica | WinUI 3 (ligera ventaja estética) |
| Riesgo de empaquetado/distribución fiable (prioridad 7 y 8 del encargo) | Bajo — MSI/WiX es terreno muy conocido | Medio-alto — MSIX + Windows App SDK añade piezas móviles adicionales a verificar en Windows 10 | **WPF** |

**Decisión: WPF sobre .NET 10, patrón MVVM**, usando Fluent Design (colores, tipografía, iconografía) como referencia visual pero implementado a mano con ResourceDictionaries — no XAML de WinUI. Se revisará esta decisión si, durante las pruebas reales en Windows, WPF muestra algún problema de estabilidad no anticipado.

## 3. Arquitectura de procesos

```
┌─────────────────────────────┐        JSON Lines por stdin/stdout       ┌──────────────────────────────┐
│   LimbusSplitPro.exe (C#)   │ ────────────────────────────────────────▶│  engine.exe (Python embebido) │
│   WPF + MVVM                │◀──────────────────────────────────────── │  separación + orquestación    │
│   - UI                      │        eventos de progreso, stage,       │  de modelos                   │
│   - NAudio WASAPI mixer     │        error, resultado                  │                                │
│   - Gestión de modelos      │                                          │  stderr → log técnico separado│
└─────────────────────────────┘                                          └──────────────────────────────┘
```

- Comunicación: **JSON Lines sobre stdin/stdout** de un proceso hijo lanzado con `ProcessStartInfo.ArgumentList` (nunca concatenación de cadenas ni `cmd.exe`).
- `stdout` reservado exclusivamente para eventos estructurados (`{"event":"progress","stage":"vocals","pct":42}`, `{"event":"error","code":"MODEL_HASH_MISMATCH", ...}`, `{"event":"result","files":[...]}`).
- `stderr` para logs técnicos de Python/PyTorch, nunca mostrados directamente al usuario — se guardan en el log rotativo y la UI muestra un mensaje traducido.
- Cancelación: señal de cierre controlada al proceso hijo + limpieza de procesos nietos (workers de PyTorch) antes de salir; verificado en cierre de la app para no dejar `python.exe` huérfano.
- El motor Python no se instala en el PATH del usuario ni depende de ningún Python de sistema — corre desde su propio `PYTHONHOME` embebido bajo la carpeta de instalación (solo lectura) con cachés redirigidas a `%LOCALAPPDATA%\Limbus Split Pro\Cache`.

## 4. Interfaz de backend de separación (para no acoplar la UI a un modelo concreto)

```csharp
public interface ISeparationBackend
{
    string Id { get; }                    // "spleeter", "demucs-htdemucs", "openunmix-umxhq"...
    IReadOnlyList<StemCapability> Capabilities { get; }
    Task<SeparationResult> RunAsync(SeparationRequest request, IProgress<StageProgress> progress, CancellationToken ct);
}
```

El backend activo por build (pública vs. desarrollo) se resuelve contra el **manifiesto de modelos verificado** (sección 7 del encargo), no está hardcodeado. Así, cuando se resuelva la licencia de los pesos de Demucs, activarlo es una entrada nueva en el manifiesto + verificación de hash, no un cambio de arquitectura ni de UI.

## 5. Estructura de repositorio propuesta

```
LimbusSplitPro/
├── src/
│   ├── LimbusSplitPro.App/          # WPF, MVVM, punto de entrada
│   ├── LimbusSplitPro.Audio/        # NAudio: mixer WASAPI, exportación offline
│   ├── LimbusSplitPro.Engine/       # cliente C# del proceso Python (IPC JSON Lines)
│   └── LimbusSplitPro.Models/       # manifiesto de modelos, verificador fail-closed, PROVENANCE
├── engine-py/
│   ├── limbus_engine/               # paquete Python: orquestación de backends de separación
│   ├── backends/                    # spleeter_backend.py, openunmix_backend.py, demucs_backend.py (dev-only)
│   └── requirements.lock            # versiones fijadas con hash
├── runtime/
│   └── python-embed/                # script reproducible de ensamblado del Python embebido + PROVENANCE.json
├── installer/
│   └── wix/                         # definición WiX v5 del MSI
├── build/
│   └── ci/                          # pipeline GitHub Actions windows-latest
├── legal/
│   ├── THIRD_PARTY_NOTICES.txt
│   ├── model-manifest.json
│   └── licenses/
├── tests/
│   ├── LimbusSplitPro.Tests/        # unit/integration C#
│   └── audio-fixtures/              # grabaciones reales multipista para pruebas (no sintéticas)
└── docs/
    ├── 01-modelos-licencias.md
    ├── 02-arquitectura-decision.md
    └── plan-pruebas-windows.md
```

## 6. Qué entrego a continuación
Con esta decisión fijada, el siguiente paso natural es crear el andamiaje real de archivos (`.sln`, proyectos `.csproj`, `PROVENANCE.json` de ejemplo, `model-manifest.schema.json`, el pipeline de CI de GitHub Actions para `windows-latest`) para que puedas clonarlo y compilarlo tú mismo en una máquina Windows real — que es, tal como expliqué antes, donde tiene que ocurrir la primera compilación verificable de verdad.

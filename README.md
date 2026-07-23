# Limbus Split Pro — Estado del proyecto

## Qué es este repositorio ahora mismo
Andamiaje real de arquitectura para Limbus Split Pro: proyectos .NET (WPF/MVVM), motor
Python intercambiable por backend, manifiesto de modelos con verificador fail-closed,
y pipeline de CI preparado para `windows-latest`. **No es un ejecutable compilado ni probado.**

## Lo que este repo puede hacer hoy
- Clonarse y abrirse en Visual Studio 2026 / `dotnet build` en una máquina Windows real.
- Servir de base real para implementar la UI, el mixer y los backends de separación.

## Lo que NO se ha hecho todavía, y por qué
| Pendiente | Motivo |
|---|---|
| Compilar y ejecutar el `.sln` | Este entorno de trabajo es Linux sin Windows; no puedo compilar WPF ni verificar WASAPI aquí. |
| Descargar y verificar pesos de Spleeter/Open-Unmix | Este sandbox no tiene acceso de red a los hosts de esos pesos (solo a registries de paquetes). `legal/model-manifest.json` tiene hashes en placeholder, NO reales — no los uses tal cual. |
| Firma Authenticode | Requiere un certificado de firma de código que debes adquirir tú; no lo tengo ni puedo generarlo. |
| Instalador WiX real | `installer/wix/` está vacío a propósito: se construye una vez exista una build funcional que empaquetar. |
| Pruebas de audio reales (sincronización, seek, exportación) | Requieren grabaciones multipista reales y un dispositivo de audio Windows; ver `tests/audio-fixtures/` (vacío, pendiente de que aportes o yo busque grabaciones con licencia adecuada para pruebas). |
| Confirmación legal de Demucs/MDX-Net | Ver `docs/01-modelos-licencias.md` — bloqueados hasta respuesta de los mantenedores. |

## Cómo compilar (en Windows real)
```powershell
dotnet restore LimbusSplitPro.sln
dotnet build LimbusSplitPro.sln -c Release -r win-x64
dotnet test tests/LimbusSplitPro.Tests/LimbusSplitPro.Tests.csproj -c Release
```

## Siguiente paso recomendado
1. Tú (o yo, si me das acceso a un runner Windows vía GitHub Actions en tu repo) ejecutas
   `dotnet restore`/`build` por primera vez y fijamos `packages.lock.json` con versiones reales.
2. En paralelo, defino contigo el mockup visual (paleta, tipografía) antes de implementar
   `MainWindow.xaml` de verdad — ahora mismo es un placeholder de dos paneles sin diseño.
3. Descargamos y verificamos (hash real) los pesos de Spleeter/Open-Unmix para completar
   `legal/model-manifest.json` con datos reales.

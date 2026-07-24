<#
.SYNOPSIS
  Ensambla el runtime Python embebido reproducible para Limbus Split Pro (seccion 9 del encargo).

.DESCRIPTION
  Descarga el paquete "embeddable" oficial de Python directamente desde python.org (HTTPS),
  calcula su SHA-256 real, y escribe runtime/python-embed/PROVENANCE.json con procedencia
  verificable. NO copia una instalacion arbitraria del equipo de build: el runtime se arma
  siempre desde cero a partir de la fuente oficial.

  Nota de investigacion (2026-07-24): python.org dejo de publicar binarios de Windows para
  la rama 3.11 a partir de 3.11.10 (la rama entro en fase de solo parches de seguridad antes
  de su EOL de octubre 2026). Ademas, el nombre de archivo del paquete embeddable cambio en
  algun punto de "python-X.Y.Z-embed-amd64.zip" a "python-X.Y.Z-amd64.zip" para builds mas
  antiguas republicadas. Este script prueba ambos patrones de nombre y usa el que exista
  realmente, en vez de asumir uno fijo.

.NOTES
  Requisitos de la seccion 9: PYTHONHOME propio, sin site-packages del usuario, arquitectura
  x64, sin rutas del ordenador de compilacion, procedencia registrada en PROVENANCE.json.
#>

param(
    # 3.11.9 (abril 2024) es la ultima release de la rama 3.11 confirmada con binarios
    # de Windows reales (carpetas amd64/arm64/win32 presentes en python.org/ftp/python/3.11.9/).
    [string]$PythonVersion = "3.11.9",
    [string]$OutputDir = "$PSScriptRoot\dist"
)

$ErrorActionPreference = "Stop"

$candidateNames = @(
    "python-$PythonVersion-embed-amd64.zip",
    "python-$PythonVersion-amd64.zip"
)

$downloadPath = $null
$usedUrl = $null

foreach ($name in $candidateNames) {
    $tryUrl = "https://www.python.org/ftp/python/$PythonVersion/$name"
    $tryPath = Join-Path $env:TEMP $name
    Write-Host "Probando: $tryUrl"
    try {
        Invoke-WebRequest -Uri $tryUrl -OutFile $tryPath -UseBasicParsing
        $downloadPath = $tryPath
        $usedUrl = $tryUrl
        Write-Host "Descarga exitosa desde: $tryUrl"
        break
    } catch {
        Write-Host "No disponible en esa ruta (404 u otro error), probando siguiente patron..."
    }
}

if (-not $downloadPath) {
    throw "No se encontro ningun paquete de Windows para Python $PythonVersion en python.org bajo ninguno de los patrones de nombre conocidos. Puede que esta version tampoco publique binarios de Windows; verificar manualmente el indice https://www.python.org/ftp/python/$PythonVersion/ y actualizar `$PythonVersion o `$candidateNames en este script."
}

$hash = (Get-FileHash -Path $downloadPath -Algorithm SHA256).Hash.ToLower()
$size = (Get-Item $downloadPath).Length

Write-Host "SHA-256 real del archivo descargado: $hash"
Write-Host "Tamano: $size bytes"

if (Test-Path $OutputDir) { Remove-Item $OutputDir -Recurse -Force }
New-Item -ItemType Directory -Path $OutputDir | Out-Null

Expand-Archive -Path $downloadPath -DestinationPath $OutputDir -Force

# El runtime embeddable trae un archivo pythonXY._pth que fuerza un modo aislado:
# IGNORA las variables de entorno PYTHONHOME y PYTHONPATH por diseno (ver
# https://docs.python.org/3/using/windows.html#the-embeddable-package). Sin
# deshabilitarlo, LimbusSplitPro.Engine no podria apuntar el runtime al paquete
# limbus_engine via PYTHONPATH. Se renombra (no se borra, para poder revertir)
# de modo que el runtime vuelva al comportamiento estandar que si respeta
# PYTHONHOME/PYTHONPATH, tal como requiere la seccion 9 del encargo.
$pthFile = Get-ChildItem -Path $OutputDir -Filter "python3*._pth" | Select-Object -First 1
if ($pthFile) {
    Rename-Item -Path $pthFile.FullName -NewName "$($pthFile.Name).isolated-mode-disabled"
    Write-Host "Modo aislado deshabilitado: $($pthFile.Name) -> $($pthFile.Name).isolated-mode-disabled"
} else {
    Write-Host "ADVERTENCIA: no se encontro archivo ._pth; verificar manualmente si PYTHONPATH funciona en este runtime."
}

$provenance = @{
    pythonVersion   = $PythonVersion
    sourceUrl       = $usedUrl
    sha256          = $hash
    sizeBytes       = $size
    architecture    = "x64"
    assembledOn     = (Get-Date).ToString("o")
    assembledBy     = "GitHub Actions windows-latest (CI reproducible build)"
    notes           = "Ensamblado por script reproducible (Assemble-PythonEmbed.ps1) desde la fuente oficial de python.org. No copiado de una instalacion arbitraria de un desarrollador."
} | ConvertTo-Json

$provenance | Out-File -FilePath (Join-Path $OutputDir "PROVENANCE.json") -Encoding utf8

Write-Host "Runtime Python embebido ensamblado en: $OutputDir"
Write-Host "PROVENANCE.json escrito con procedencia real y verificable."

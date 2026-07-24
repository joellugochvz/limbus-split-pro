<#
.SYNOPSIS
  Ensambla el runtime Python embebido reproducible para Limbus Split Pro (sección 9 del encargo).

.DESCRIPTION
  Descarga el paquete "embeddable" oficial de Python directamente desde python.org (HTTPS),
  calcula su SHA-256 real, y escribe runtime/python-embed/PROVENANCE.json con procedencia
  verificable. NO copia una instalación arbitraria del equipo de build: el runtime se arma
  siempre desde cero a partir de la fuente oficial.

  Este script está pensado para ejecutarse en el runner windows-latest de GitHub Actions,
  que sí tiene salida de red hacia python.org (a diferencia del entorno de desarrollo usado
  para generar el resto de este repositorio, que no la tiene).

.NOTES
  Requisitos de la sección 9: PYTHONHOME propio, sin site-packages del usuario, arquitectura
  x64, sin rutas del ordenador de compilación, procedencia registrada en PROVENANCE.json.
#>

param(
    [string]$PythonVersion = "3.11.15",
    [string]$OutputDir = "$PSScriptRoot\dist"
)

$ErrorActionPreference = "Stop"

$arch = "amd64"
$fileName = "python-$PythonVersion-embed-$arch.zip"
$url = "https://www.python.org/ftp/python/$PythonVersion/$fileName"
$downloadPath = Join-Path $env:TEMP $fileName

Write-Host "Descargando runtime Python embebido oficial desde: $url"
Invoke-WebRequest -Uri $url -OutFile $downloadPath -UseBasicParsing

$hash = (Get-FileHash -Path $downloadPath -Algorithm SHA256).Hash.ToLower()
$size = (Get-Item $downloadPath).Length

Write-Host "SHA-256 real del archivo descargado: $hash"
Write-Host "Tamaño: $size bytes"

if (Test-Path $OutputDir) { Remove-Item $OutputDir -Recurse -Force }
New-Item -ItemType Directory -Path $OutputDir | Out-Null

Expand-Archive -Path $downloadPath -DestinationPath $OutputDir -Force

# El embeddable de Python trae pip deshabilitado y sin site-packages de usuario por defecto;
# se fuerza explícitamente vía PROVENANCE + variables de entorno en tiempo de ejecución
# (PYTHONHOME/PYTHONNOUSERSITE), no se modifica aquí el contenido del zip oficial.

$provenance = @{
    pythonVersion   = $PythonVersion
    sourceUrl       = $url
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

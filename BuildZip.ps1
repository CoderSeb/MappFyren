[CmdletBinding()]
param(
  [Parameter(Mandatory)]
  [ValidatePattern('^\d+\.\d+\.\d+(-[0-9A-Za-z\.-]+)?$')]
  [string]$Version,

  [Parameter(Mandatory)]
  [string]$OutputDir,

  [string]$Project = ".\MappFyren.App\MappFyren.App.csproj",
  [string]$Configuration = "Release",

  # För "en enda exe" krävs runtime + self-contained. Default är OFF (mindre output men kräver .NET Desktop Runtime).
  [switch]$SelfContained,
  [switch]$SingleFile,

  # Vid SelfContained/SingleFile: välj runtime
  [ValidateSet("win-x64","win-x86","win-arm64")]
  [string]$Runtime = "win-x64"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

function Require-Command([string]$Name) {
  if (-not (Get-Command $Name -ErrorAction SilentlyContinue)) {
    throw "Hittar inte '$Name' i PATH."
  }
}

Require-Command dotnet

$repoRoot = (Resolve-Path ".").Path
$projectPath = (Resolve-Path $Project).Path
$projectDir = Split-Path -Parent $projectPath

# Skapa outputdir om den inte finns
$OutputDir = (Resolve-Path -LiteralPath $OutputDir -ErrorAction SilentlyContinue)?.Path ?? $OutputDir
New-Item -ItemType Directory -Force -Path $OutputDir | Out-Null

$releaseName = "release-$Version"
$stagingDir = Join-Path $OutputDir $releaseName
$zipPath = Join-Path $OutputDir "$releaseName.zip"

# Temporär publish-mapp (i outputdir, inte i repo)
$publishDir = Join-Path $OutputDir "_publish_$Version"
if (Test-Path $publishDir) { Remove-Item $publishDir -Recurse -Force }
New-Item -ItemType Directory -Force -Path $publishDir | Out-Null

# Staging reset
if (Test-Path $stagingDir) { Remove-Item $stagingDir -Recurse -Force }
New-Item -ItemType Directory -Force -Path $stagingDir | Out-Null

if (Test-Path $zipPath) { Remove-Item $zipPath -Force }

Write-Host "==> Restore + Build ($Configuration)" -ForegroundColor Cyan
dotnet restore $projectPath | Out-Host
dotnet build $projectPath -c $Configuration | Out-Host

Write-Host "==> Publish -> $publishDir" -ForegroundColor Cyan

# Bygg publish-argument
$publishArgs = @("publish", $projectPath, "-c", $Configuration, "-o", $publishDir, "-p:Version=$Version")

if ($SelfContained -or $SingleFile) {
  # För single-file behöver vi runtime, och self-contained är starkt rekommenderat
  $publishArgs += @("-r", $Runtime, "--self-contained", "true")
} else {
  $publishArgs += @("--self-contained", "false")
}

if ($SingleFile) {
  $publishArgs += @(
    "-p:PublishSingleFile=true",
    "-p:IncludeNativeLibrariesForSelfExtract=true",
    "-p:DebugType=None",
    "-p:DebugSymbols=false"
  )
}

dotnet @publishArgs | Out-Host

# Kopiera publish-output (ALLT som krävs för att köra)
Write-Host "==> Kopierar publish-filer till staging" -ForegroundColor Cyan
Copy-Item -Path (Join-Path $publishDir "*") -Destination $stagingDir -Recurse -Force

# Säkerställ settings.json (om den inte kom med i publish av någon anledning)
$settingsInPublish = Join-Path $stagingDir "settings.json"
$settingsInProject = Join-Path $projectDir "settings.json"

if (-not (Test-Path $settingsInPublish) -and (Test-Path $settingsInProject)) {
  Copy-Item -Path $settingsInProject -Destination $settingsInPublish -Force
}

# Zip
Write-Host "==> Skapar zip: $zipPath" -ForegroundColor Cyan
Compress-Archive -Path (Join-Path $stagingDir "*") -DestinationPath $zipPath -Force

# Städa temp publish
Remove-Item $publishDir -Recurse -Force

Write-Host "`nKLART!" -ForegroundColor Green
Write-Host "Staging: $stagingDir"
Write-Host "Zip:     $zipPath"

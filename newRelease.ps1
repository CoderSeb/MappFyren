[CmdletBinding()]
param(
  [Parameter(Mandatory = $true)]
  [ValidatePattern('^\d+\.\d+\.\d+(-[0-9A-Za-z\.-]+)?$')]
  [string]$Version,

  [string]$Project = ".\MappFyren.App\MappFyren.App.csproj",
  [string]$Configuration = "Release",
  [string]$Runtime = "win-x64",
  [switch]$DraftRelease,
  [switch]$Prerelease
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

function Require-Command([string]$Name) {
  if (-not (Get-Command $Name -ErrorAction SilentlyContinue)) {
    throw "Hittar inte '$Name' i PATH. Installera/aktivera och försök igen."
  }
}

Require-Command git
Require-Command dotnet
Require-Command gh

# --- Paths
$repoRoot = (Resolve-Path ".").Path
$artifactsRoot = Join-Path $repoRoot "artifacts"
$releaseFolderName = "release-$Version"
$stagingDir = Join-Path $artifactsRoot $releaseFolderName
$zipPath = Join-Path $artifactsRoot "$releaseFolderName.zip"

# --- Basic git sanity
$branch = (git rev-parse --abbrev-ref HEAD).Trim()
if ($branch -ne "main") {
  throw "Du står på branch '$branch'. Byt till 'main' innan release (git checkout main)."
}

# Se till att vi har remote
$remotes = (git remote).Trim()
if ([string]::IsNullOrWhiteSpace($remotes)) {
  throw "Ingen git remote hittad. Kör: git remote add origin <url>"
}

# Varna om smutsigt repo
$dirty = (git status --porcelain)
if (-not [string]::IsNullOrWhiteSpace($dirty)) {
  throw "Du har ocommittade ändringar. Committa/stasha innan release.`n$dirty"
}

Write-Host "==> Bygger & publishar $Version" -ForegroundColor Cyan

# Rensa artifacts
New-Item -ItemType Directory -Force -Path $artifactsRoot | Out-Null
if (Test-Path $stagingDir) { Remove-Item $stagingDir -Recurse -Force }
if (Test-Path $zipPath) { Remove-Item $zipPath -Force }

# Restore + Build
dotnet restore $Project | Out-Host
dotnet build $Project -c $Configuration | Out-Host

# Publish (single-file, self-contained)
dotnet publish $Project -c $Configuration -r $Runtime --self-contained true `
  -p:PublishSingleFile=true `
  -p:IncludeNativeLibrariesForSelfExtract=true `
  -p:DebugType=None `
  -p:DebugSymbols=false `
  -p:Version=$Version `
  -p:AssemblyVersion="$Version.0" `
  -p:FileVersion="$Version.0" | Out-Host

$publishDir = Join-Path (Split-Path -Parent (Resolve-Path $Project)) "bin\$Configuration\net8.0-windows\$Runtime\publish"
if (-not (Test-Path $publishDir)) {
  throw "Publish-mappen hittas inte: $publishDir"
}

# Hitta exe i publish-mappen (tar den största .exe)
$exe = Get-ChildItem -Path $publishDir -Filter *.exe | Sort-Object Length -Descending | Select-Object -First 1
if (-not $exe) {
  throw "Hittar ingen .exe i $publishDir"
}

# settings.json: helst från publish, annars från projektmapp
$settingsFromPublish = Join-Path $publishDir "settings.json"
$settingsFromProject = Join-Path (Split-Path -Parent (Resolve-Path $Project)) "settings.json"

New-Item -ItemType Directory -Force -Path $stagingDir | Out-Null

Copy-Item -Path $exe.FullName -Destination (Join-Path $stagingDir $exe.Name) -Force

if (Test-Path $settingsFromPublish) {
  Copy-Item -Path $settingsFromPublish -Destination (Join-Path $stagingDir "settings.json") -Force
}
elseif (Test-Path $settingsFromProject) {
  Copy-Item -Path $settingsFromProject -Destination (Join-Path $stagingDir "settings.json") -Force
}
else {
  Write-Warning "Hittar ingen settings.json (varken i publish eller projekt). Release blir utan settings.json."
}

# Zip
Write-Host "==> Skapar zip: $zipPath" -ForegroundColor Cyan
Compress-Archive -Path (Join-Path $stagingDir "*") -DestinationPath $zipPath -Force

# --- Git tag + push
$tag = "v$Version"

# Stoppa om tag redan finns lokalt eller remote
$existingLocalTag = (git tag -l $tag).Trim()
if (-not [string]::IsNullOrWhiteSpace($existingLocalTag)) {
  throw "Tag '$tag' finns redan lokalt. Välj ett nytt versionsnummer eller ta bort taggen."
}

$existingRemoteTag = (git ls-remote --tags origin $tag) 2>$null
if (-not [string]::IsNullOrWhiteSpace($existingRemoteTag)) {
  throw "Tag '$tag' finns redan på origin. Välj ett nytt versionsnummer."
}

Write-Host "==> Skapar git-tag: $tag" -ForegroundColor Cyan
git tag -a $tag -m "Release $Version" | Out-Host
git push origin $tag | Out-Host

# --- GitHub Release (asset-only: din zip laddas upp; GitHub visar även source archives automatiskt)
Write-Host "==> Skapar GitHub Release och laddar upp asset" -ForegroundColor Cyan

$notes = @"
Release $Version

Ladda ner: $releaseFolderName.zip (asset)
"@

$flags = @()
if ($DraftRelease) { $flags += "--draft" }
if ($Prerelease) { $flags += "--prerelease" }

gh release create $tag $zipPath `
  --title "$tag" `
  --notes $notes `
  @flags | Out-Host

Write-Host "`nKLART!" -ForegroundColor Green
Write-Host "Publish:  $publishDir"
Write-Host "Staging:  $stagingDir"
Write-Host "Zip:      $zipPath"
Write-Host "Release:  GitHub -> Releases -> $tag"

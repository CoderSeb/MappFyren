[CmdletBinding()]
param(
  [Parameter(Mandatory = $true)]
  [ValidatePattern('^\d+\.\d+\.\d+(-[0-9A-Za-z\.-]+)?$')]
  [string]$Version,

  [string]$Project = ".\MappFyren.App\MappFyren.App.csproj",
  [string]$Configuration = "Release",
  [string]$Runtime = "win-x64",

  # Om du vill att scriptet ska committa automatiskt om du har ändringar:
  [string]$CommitMessage,

  [switch]$DraftRelease,
  [switch]$Prerelease
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

function Require-Command([string]$Name) {
  if (-not (Get-Command $Name -ErrorAction SilentlyContinue)) {
    throw "Hittar inte '$Name' i PATH."
  }
}

function Git-String([string[]]$Args) {
  # Kör git och returnera alltid en string (aldrig $null)
  return (& git @Args | Out-String).Trim()
}

Require-Command git
Require-Command dotnet
Require-Command gh

# --- Se till att vi står på main
$branch = Git-String @("rev-parse", "--abbrev-ref", "HEAD")
if ($branch -ne "main") {
  throw "Du står på branch '$branch'. Byt till 'main' innan release: git checkout main"
}

# --- Se till att remote finns
$remotes = Git-String @("remote")
if ([string]::IsNullOrWhiteSpace($remotes)) {
  throw "Ingen git remote hittad. Kör: git remote add origin <url>"
}

# --- Om repo är smutsigt: committa om CommitMessage finns, annars stoppa
$dirty = & git status --porcelain
if (-not [string]::IsNullOrWhiteSpace(($dirty | Out-String).Trim())) {
  if ([string]::IsNullOrWhiteSpace($CommitMessage)) {
    throw "Du har ocommittade ändringar. Antingen committa manuellt, eller kör scriptet med -CommitMessage `"Din text`"."
  }

  Write-Host "==> Repo har ändringar, committar..." -ForegroundColor Cyan
  git add -A | Out-Host
  git commit -m $CommitMessage | Out-Host
  git push origin main | Out-Host
}

# --- Kontrollera gh auth (ger bättre fel tidigt)
try {
  & gh auth status | Out-Null
} catch {
  throw "GitHub CLI verkar inte vara inloggad. Kör: gh auth login"
}

# --- Paths
$repoRoot = (Resolve-Path ".").Path
$artifactsRoot = Join-Path $repoRoot "artifacts"
$releaseFolderName = "release-$Version"
$stagingDir = Join-Path $artifactsRoot $releaseFolderName
$zipPath = Join-Path $artifactsRoot "$releaseFolderName.zip"

New-Item -ItemType Directory -Force -Path $artifactsRoot | Out-Null
if (Test-Path $stagingDir) { Remove-Item $stagingDir -Recurse -Force }
if (Test-Path $zipPath) { Remove-Item $zipPath -Force }

Write-Host "==> Bygger & publishar $Version" -ForegroundColor Cyan

dotnet restore $Project | Out-Host
dotnet build $Project -c $Configuration | Out-Host

dotnet publish $Project -c $Configuration -r $Runtime --self-contained true `
  -p:PublishSingleFile=true `
  -p:IncludeNativeLibrariesForSelfExtract=true `
  -p:DebugType=None `
  -p:DebugSymbols=false `
  -p:Version=$Version `
  -p:AssemblyVersion="$Version.0" `
  -p:FileVersion="$Version.0" | Out-Host

$projectDir = Split-Path -Parent (Resolve-Path $Project)
$publishDir = Join-Path $projectDir "bin\$Configuration\net8.0-windows\$Runtime\publish"

if (-not (Test-Path $publishDir)) {
  throw "Publish-mappen hittas inte: $publishDir"
}

$exe = Get-ChildItem -Path $publishDir -Filter *.exe | Sort-Object Length -Descending | Select-Object -First 1
if (-not $exe) {
  throw "Hittar ingen .exe i $publishDir"
}

$settingsFromPublish = Join-Path $publishDir "settings.json"
$settingsFromProject = Join-Path $projectDir "settings.json"

New-Item -ItemType Directory -Force -Path $stagingDir | Out-Null

Copy-Item -Path $exe.FullName -Destination (Join-Path $stagingDir $exe.Name) -Force

if (Test-Path $settingsFromPublish) {
  Copy-Item -Path $settingsFromPublish -Destination (Join-Path $stagingDir "settings.json") -Force
}
elseif (Test-Path $settingsFromProject) {
  Copy-Item -Path $settingsFromProject -Destination (Join-Path $stagingDir "settings.json") -Force
}
else {
  Write-Warning "Hittar ingen settings.json (varken i publish eller projekt)."
}

Write-Host "==> Skapar zip: $zipPath" -ForegroundColor Cyan
Compress-Archive -Path (Join-Path $stagingDir "*") -DestinationPath $zipPath -Force

# --- Tag + push
$tag = "v$Version"

$existingLocalTag = Git-String @("tag", "-l", $tag)
if (-not [string]::IsNullOrWhiteSpace($existingLocalTag)) {
  throw "Tag '$tag' finns redan lokalt. Välj ett nytt versionsnummer eller ta bort taggen."
}

$existingRemoteTag = Git-String @("ls-remote", "--tags", "origin", $tag)
if (-not [string]::IsNullOrWhiteSpace($existingRemoteTag)) {
  throw "Tag '$tag' finns redan på origin. Välj ett nytt versionsnummer."
}

Write-Host "==> Skapar git-tag: $tag" -ForegroundColor Cyan
git tag -a $tag -m "Release $Version" | Out-Host
git push origin $tag | Out-Host

# --- GitHub Release + asset
Write-Host "==> Skapar GitHub Release och laddar upp zip-asset" -ForegroundColor Cyan

$notes = @"
Release $Version

Ladda ner asseten: $releaseFolderName.zip
"@

$ghArgs = @("release", "create", $tag, $zipPath, "--title", $tag, "--notes", $notes)
if ($DraftRelease) { $ghArgs += "--draft" }
if ($Prerelease) { $ghArgs += "--prerelease" }

& gh @ghArgs | Out-Host

Write-Host "`nKLART!" -ForegroundColor Green
Write-Host "Publish:  $publishDir"
Write-Host "Staging:  $stagingDir"
Write-Host "Zip:      $zipPath"
Write-Host "Release:  GitHub -> Releases -> $tag"

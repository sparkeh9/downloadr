Param(
  [string]$Configuration = "Release",
  [string]$VersionTag = ""
)

$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$repo = Resolve-Path (Join-Path $root "..")
Set-Location $repo

$dist = Join-Path $repo "dist"
if (Test-Path $dist) { Remove-Item $dist -Recurse -Force }
New-Item $dist -ItemType Directory | Out-Null

$project = "src/Downloadr.Cli/Downloadr.Cli.csproj"

$targets = @(
  @{ Rid = "win-x64"; Ext = ".zip" },
  @{ Rid = "linux-x64"; Ext = ".tar.gz" }
)

$chosenVersion = $VersionTag
if (-not $chosenVersion -or $chosenVersion -eq "") {
  $input = Read-Host "Enter version (e.g. v1.2.3). Press Enter for 0.0.0-dev"
  if (-not $input -or $input -eq "") { $input = "0.0.0-dev" }
  $chosenVersion = $input
}

$versionSuffix = "-" + $chosenVersion

# Derive assembly version properties
$pVersion = $chosenVersion
$fileVersion = ($chosenVersion -replace "[^0-9\.].*$", "")
if (-not $fileVersion) { $fileVersion = "0.0.0.0" }

foreach ($t in $targets) {
  $outDir = Join-Path $dist $t.Rid
  dotnet publish $project -c $Configuration -r $t.Rid --self-contained true /p:PublishSingleFile=true /p:AssemblyName=downloadr /p:Version=$pVersion /p:FileVersion=$fileVersion /p:AssemblyVersion=$fileVersion -o $outDir

  Push-Location $dist
  if ($t.Ext -eq ".zip") {
    $zip = "downloadr-" + $t.Rid + $versionSuffix + ".zip"
    if (Test-Path $zip) { Remove-Item $zip -Force }
    Compress-Archive -Path $t.Rid -DestinationPath $zip
  } else {
    $tar = "downloadr-" + $t.Rid + $versionSuffix + ".tar.gz"
    if (Test-Path $tar) { Remove-Item $tar -Force }
    tar -czf $tar $($t.Rid)
  }
  Pop-Location
}

Write-Host "Artifacts created under $dist"



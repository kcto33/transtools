param(
  [string]$Configuration = "Release",
  [string]$Runtime = "win-x64",
  [string]$VersionSuffix = "",
  [switch]$NoZip
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$projectPath = Join-Path $repoRoot "ScreenTranslator\ScreenTranslator.csproj"
$packageRoot = Join-Path $repoRoot "artifacts\packages"
$publishExeName = "transtools.exe"

$dateStamp = Get-Date -Format "yyyyMMdd"
$nameParts = @("transtools", $Runtime, "onefile", $dateStamp)
if ($VersionSuffix)
{
  $nameParts += $VersionSuffix
}

$packageName = ($nameParts | Where-Object { $_ }) -join "-"
$publishDir = Join-Path $packageRoot $packageName
$zipPath = "$publishDir.zip"

if (Test-Path $publishDir)
{
  Remove-Item $publishDir -Recurse -Force
}

New-Item -ItemType Directory -Path $publishDir -Force | Out-Null

$restoreArgs = @(
  "restore",
  $projectPath,
  "-r", $Runtime
)

& dotnet @restoreArgs
if ($LASTEXITCODE -ne 0)
{
  throw "dotnet restore failed with exit code $LASTEXITCODE."
}

$publishArgs = @(
  "publish",
  $projectPath,
  "-c", $Configuration,
  "-r", $Runtime,
  "--self-contained", "true",
  "-p:PublishSingleFile=true",
  "-p:IncludeNativeLibrariesForSelfExtract=true",
  "-p:EnableCompressionInSingleFile=true",
  "-p:DebugType=None",
  "-p:DebugSymbols=false",
  "-o", $publishDir
)

& dotnet @publishArgs
if ($LASTEXITCODE -ne 0)
{
  throw "dotnet publish failed with exit code $LASTEXITCODE."
}

$exePath = Join-Path $publishDir $publishExeName
if (-not (Test-Path $exePath))
{
  throw "Publish succeeded but $publishExeName was not found at $exePath."
}

if (-not $NoZip)
{
  if (Test-Path $zipPath)
  {
    Remove-Item $zipPath -Force
  }

  & tar.exe -a -cf $zipPath -C $publishDir .
  if ($LASTEXITCODE -ne 0)
  {
    throw "zip packaging failed with exit code $LASTEXITCODE."
  }
}

Write-Host "EXE: $exePath"
if (-not $NoZip)
{
  Write-Host "ZIP: $zipPath"
}

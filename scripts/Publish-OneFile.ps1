param(
  [string]$Configuration = "Release",
  [string]$Runtime = "win-x64",
  [string]$Version = "",
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

if ($Version)
{
  $publishArgs += "-p:Version=$Version"
  $publishArgs += "-p:AssemblyVersion=$Version"
  $publishArgs += "-p:FileVersion=$Version"
  $publishArgs += "-p:IncludeSourceRevisionInInformationalVersion=false"
}

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

  Add-Type -AssemblyName System.IO.Compression.FileSystem
  [System.IO.Compression.ZipFile]::CreateFromDirectory(
    $publishDir,
    $zipPath,
    [System.IO.Compression.CompressionLevel]::Optimal,
    $false)

  $archive = $null
  try
  {
    $archive = [System.IO.Compression.ZipFile]::OpenRead($zipPath)
    $exeEntry = $archive.Entries | Where-Object { $_.FullName -eq $publishExeName } | Select-Object -First 1
    if (-not $exeEntry)
    {
      throw "zip package verification failed because $publishExeName was not found."
    }
  }
  finally
  {
    if ($archive)
    {
      $archive.Dispose()
    }
  }
}

Write-Host "EXE: $exePath"
if (-not $NoZip)
{
  Write-Host "ZIP: $zipPath"
}

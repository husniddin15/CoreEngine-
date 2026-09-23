<#
.SYNOPSIS
    Downloads arduino-cli and installs the Arduino AVR core into tools/arduino/.

.DESCRIPTION
    Creates the offline compile toolchain described in docs/05-arduino-emulation-spec.md §8
    and ADR-0003. Everything is written under tools/arduino/, which is not committed to Git.

    Sources (official only):
      - arduino-cli from the GitHub releases of arduino/arduino-cli, verified against the
        release's SHA-256 checksum file.
      - The arduino:avr core, avr-gcc and avrdude, downloaded by arduino-cli from
        downloads.arduino.cc (arduino-cli verifies their checksums from the package index).

    Safe to run again: steps that are already done are skipped.

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File tools\fetch-toolchain.ps1
#>
param(
    [string]$CliVersion = "1.5.1",
    [string]$AvrCoreVersion = "1.8.8"
)

$ErrorActionPreference = "Stop"
$ProgressPreference = "SilentlyContinue"   # Invoke-WebRequest is much faster without the progress bar

$toolsDir   = Split-Path -Parent $MyInvocation.MyCommand.Path
$baseDir    = Join-Path $toolsDir "arduino"
$binDir     = Join-Path $baseDir "bin"
$dataDir    = Join-Path $baseDir "data"
$userDir    = Join-Path $baseDir "user"
$stagingDir = Join-Path $baseDir "staging"
$cacheDir   = Join-Path $baseDir "cache"
$configPath = Join-Path $baseDir "arduino-cli.yaml"
$cliPath    = Join-Path $binDir "arduino-cli.exe"

foreach ($dir in @($binDir, $dataDir, $userDir, $stagingDir, $cacheDir)) {
    New-Item -ItemType Directory -Force -Path $dir | Out-Null
}

function Invoke-Cli {
    param([string[]]$CliArgs)
    & $cliPath @CliArgs --config-file $configPath
    if ($LASTEXITCODE -ne 0) { throw "arduino-cli $($CliArgs -join ' ') failed with exit code $LASTEXITCODE" }
}

# 1. arduino-cli itself
if (Test-Path $cliPath) {
    Write-Host "arduino-cli already present: $cliPath"
} else {
    $zipName    = "arduino-cli_${CliVersion}_Windows_64bit.zip"
    $releaseUrl = "https://github.com/arduino/arduino-cli/releases/download/v$CliVersion"
    $zipPath    = Join-Path $stagingDir $zipName

    Write-Host "Downloading $zipName ..."
    Invoke-WebRequest -Uri "$releaseUrl/$zipName" -OutFile $zipPath -UseBasicParsing

    Write-Host "Verifying checksum ..."
    $checksums = (Invoke-WebRequest -Uri "$releaseUrl/$CliVersion-checksums.txt" -UseBasicParsing).Content
    if ($checksums -is [byte[]]) { $checksums = [System.Text.Encoding]::UTF8.GetString($checksums) }
    $line = ($checksums -split "`n") | Where-Object { $_ -match [regex]::Escape($zipName) } | Select-Object -First 1
    if (-not $line) { throw "No checksum listed for $zipName" }
    $expected = ($line.Trim() -split '\s+')[0].ToLowerInvariant()
    $actual   = (Get-FileHash -Path $zipPath -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($expected -ne $actual) { throw "Checksum mismatch for ${zipName}: expected $expected, got $actual" }

    Expand-Archive -Path $zipPath -DestinationPath $binDir -Force
    Write-Host "arduino-cli installed: $cliPath"
}

# 2. Configuration: keep every arduino-cli directory inside tools/arduino/ and force English output.
$toSlash = { param($p) $p -replace '\\', '/' }
$yaml = @"
board_manager:
  additional_urls: []
build_cache:
  path: $(& $toSlash $cacheDir)
directories:
  data: $(& $toSlash $dataDir)
  downloads: $(& $toSlash $stagingDir)
  user: $(& $toSlash $userDir)
locale: en
updater:
  enable_notification: false
"@
[System.IO.File]::WriteAllText($configPath, $yaml, (New-Object System.Text.UTF8Encoding($false)))

# 3. The Arduino AVR core (brings avr-gcc, avr-libc and avrdude).
Write-Host "Updating the package index ..."
Invoke-Cli @("core", "update-index")
Write-Host "Installing arduino:avr@$AvrCoreVersion ..."
Invoke-Cli @("core", "install", "arduino:avr@$AvrCoreVersion")

Write-Host ""
Invoke-Cli @("version")
Invoke-Cli @("core", "list")
Write-Host "Toolchain ready in $baseDir"

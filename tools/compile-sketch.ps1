<#
.SYNOPSIS
    Compiles an Arduino sketch with the bundled toolchain (run fetch-toolchain.ps1 first).

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File tools\compile-sketch.ps1 -Sketch path\to\Blink -OutDir out
#>
param(
    [Parameter(Mandatory = $true)] [string]$Sketch,
    [string]$Fqbn = "arduino:avr:uno",
    [string]$OutDir
)

$ErrorActionPreference = "Stop"

$toolsDir   = Split-Path -Parent $MyInvocation.MyCommand.Path
$baseDir    = Join-Path $toolsDir "arduino"
$cliPath    = Join-Path $baseDir "bin\arduino-cli.exe"
$configPath = Join-Path $baseDir "arduino-cli.yaml"
if (-not (Test-Path $cliPath)) { throw "Toolchain not found. Run tools\fetch-toolchain.ps1 first." }

$sketchDir = (Resolve-Path $Sketch).Path
$name      = Split-Path -Leaf $sketchDir
$buildDir  = Join-Path $baseDir ("builds\" + $name + "-" + ($Fqbn -replace '[:=,]', '_'))
if (-not $OutDir) { $OutDir = Join-Path $buildDir "out" }
New-Item -ItemType Directory -Force -Path $buildDir, $OutDir | Out-Null

$watch = [System.Diagnostics.Stopwatch]::StartNew()
& $cliPath compile --fqbn $Fqbn --config-file $configPath --build-path $buildDir --output-dir $OutDir --warnings default $sketchDir
$exit = $LASTEXITCODE
$watch.Stop()
Write-Host ("Compile finished in {0:N2} s (exit code {1})" -f $watch.Elapsed.TotalSeconds, $exit)
exit $exit

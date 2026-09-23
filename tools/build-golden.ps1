<#
.SYNOPSIS
    Compiles the golden test sketches and copies their .hex files into the test project.

.DESCRIPTION
    Sketches live in core/CoreEngine.Sim.Tests/Golden/Sketches/<Name>/<Name>.ino.
    The compiled images go to core/CoreEngine.Sim.Tests/Golden/Hex/<Name>.hex and are committed,
    so the tests and CI run without the Arduino toolchain.
    Run tools\fetch-toolchain.ps1 once before this script.
#>
param([string]$Fqbn = "arduino:avr:uno")

$ErrorActionPreference = "Stop"

$toolsDir    = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoDir     = Split-Path -Parent $toolsDir
$goldenDir   = Join-Path $repoDir "core\CoreEngine.Sim.Tests\Golden"
$sketchesDir = Join-Path $goldenDir "Sketches"
$hexDir      = Join-Path $goldenDir "Hex"
$compile     = Join-Path $toolsDir "compile-sketch.ps1"
New-Item -ItemType Directory -Force -Path $hexDir | Out-Null

foreach ($sketch in Get-ChildItem -Path $sketchesDir -Directory) {
    $outDir = Join-Path $toolsDir ("arduino\golden\" + $sketch.Name)
    Write-Host "== $($sketch.Name)"
    & powershell -NoProfile -ExecutionPolicy Bypass -File $compile -Sketch $sketch.FullName -Fqbn $Fqbn -OutDir $outDir
    if ($LASTEXITCODE -ne 0) { throw "Compiling $($sketch.Name) failed" }
    $hex = Join-Path $outDir ($sketch.Name + ".ino.hex")
    Copy-Item -Path $hex -Destination (Join-Path $hexDir ($sketch.Name + ".hex")) -Force
}

Write-Host "Golden hex files updated in $hexDir"

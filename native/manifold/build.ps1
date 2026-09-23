<#
.SYNOPSIS
    Builds the Manifold mesh-boolean library (Apache-2.0) as one native DLL for the Unity app.

.DESCRIPTION
    ADR-0005: the Body Studio uses Manifold through its C API (manifoldc) and P/Invoke.
    1. Sparse-clones the tagged release from GitHub (source folders only, about 2 MB) into src/.
    2. Configures CMakeLists.txt in this folder with the Visual Studio 2026 C++ toolchain: the
       Manifold core as a static library, the upstream C bindings as manifoldc.dll, the static
       MSVC runtime (no Visual C++ Redistributable needed), no tests, no parallel backend, and
       the built-in "boolean2" 2D backend, so nothing else is downloaded.
    3. Builds Release and copies manifoldc.dll and the licence to app/Assets/Plugins/x86_64/.

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File native\manifold\build.ps1
#>
param([string]$Version = "v3.5.3")

$ErrorActionPreference = "Stop"

$here      = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoDir   = Split-Path -Parent (Split-Path -Parent $here)
$srcDir    = Join-Path $here "src"
$buildDir  = Join-Path $here "build"
$pluginDir = Join-Path $repoDir "app\Assets\Plugins\x86_64"

# CMake from the Visual Studio Build Tools (or Visual Studio) installation.
$vswhere = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe"
$vsPath  = & $vswhere -latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
if (-not $vsPath) { throw "Visual Studio C++ build tools not found." }
$cmake = Join-Path $vsPath "Common7\IDE\CommonExtensions\Microsoft\CMake\CMake\bin\cmake.exe"
if (-not (Test-Path $cmake)) { throw "CMake not found in $vsPath (install 'C++ CMake tools for Windows')." }
$vsMajor = (& $vswhere -latest -products * -property installationVersion).Split('.')[0]
$generator = @{ "18" = "Visual Studio 18 2026"; "17" = "Visual Studio 17 2022" }[$vsMajor]
if (-not $generator) { throw "Unsupported Visual Studio version $vsMajor." }

if (-not (Test-Path (Join-Path $srcDir "CMakeLists.txt"))) {
    Write-Host "Fetching Manifold $Version source ..."
    git clone --quiet --depth 1 --branch $Version --filter=blob:none --sparse https://github.com/elalish/manifold.git $srcDir
    if ($LASTEXITCODE -ne 0) { throw "git clone failed" }
    git -C $srcDir sparse-checkout set --no-cone /CMakeLists.txt /cmake/ /src/ /include/ /bindings/c/ /bindings/CMakeLists.txt /LICENSE
    if ($LASTEXITCODE -ne 0) { throw "git sparse-checkout failed" }
}

Write-Host "Configuring with $generator ..."
& $cmake -S $here -B $buildDir -G $generator -A x64
if ($LASTEXITCODE -ne 0) { throw "CMake configure failed" }

Write-Host "Building Release ..."
& $cmake --build $buildDir --config Release --target manifoldc --parallel
if ($LASTEXITCODE -ne 0) { throw "Build failed" }

$dll = Join-Path $buildDir "Release\manifoldc.dll"
if (-not (Test-Path $dll)) { throw "manifoldc.dll was not produced." }
New-Item -ItemType Directory -Force -Path $pluginDir | Out-Null
Copy-Item $dll -Destination $pluginDir -Force
Copy-Item (Join-Path $srcDir "LICENSE") -Destination (Join-Path $pluginDir "manifold-LICENSE.txt") -Force
Write-Host ("Manifold {0}: manifoldc.dll {1:N0} KB copied to {2}" -f $Version, ((Get-Item $dll).Length / 1KB), $pluginDir)

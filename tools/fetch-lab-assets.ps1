<#
.SYNOPSIS
    Downloads the Poly Haven assets of the Garage's robotics lab into app/Assets/ThirdParty/PolyHaven.

.DESCRIPTION
    The owner approved these files on 2026-09-24 (docs/13, D19): a lab HDRI for lighting, reflections and
    the background, a concrete floor texture and photo-scanned props. Every Poly Haven asset is CC0 (public
    domain, https://polyhaven.com/license), so the game may ship them; the files are not committed to Git
    (like the Arduino toolchain) and this script fetches them again. Each file is checked against the MD5
    that Poly Haven's API publishes.

.PARAMETER Force
    Download again even when a file is already there.
#>
param([switch]$Force)

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue' # Invoke-WebRequest is many times faster without the progress bar
$root = Join-Path $PSScriptRoot '..\app\Assets\ThirdParty\PolyHaven'
$api = 'https://api.polyhaven.com/files/'

$hdri = @{ id = 'vintage_measuring_lab'; res = '4k' }
$textures = @(
    @{ id = 'concrete_floor_02'; res = '2k'; maps = @('Diffuse', 'nor_gl', 'arm') }
)
$models = @(
    @{ id = 'WoodenTable_03'; res = '2k' },
    @{ id = 'metal_tool_chest'; res = '1k' },
    @{ id = 'steel_frame_shelves_01'; res = '1k' },
    @{ id = 'bench_vice_01'; res = '1k' },
    @{ id = 'desk_lamp_arm_01'; res = '1k' },
    @{ id = 'Drill_01'; res = '1k' },
    @{ id = 'flathead_screwdriver'; res = '1k' },
    @{ id = 'ratchet_wrench'; res = '1k' },
    @{ id = 'measuring_tape_01'; res = '1k' },
    @{ id = 'cardboard_box_01'; res = '1k' },
    @{ id = 'spray_paint_bottles'; res = '1k' },
    @{ id = 'small_oil_can_01'; res = '1k' },
    @{ id = 'wooden_stool_01'; res = '1k' }
)

$script:bytes = 0
function Get-Checked([string]$url, [string]$md5, [string]$path) {
    if (-not $Force -and (Test-Path $path)) {
        if ((Get-FileHash $path -Algorithm MD5).Hash.ToLowerInvariant() -eq $md5) { return }
    }
    New-Item -ItemType Directory -Force (Split-Path $path) | Out-Null
    Invoke-WebRequest -Uri $url -OutFile $path -UseBasicParsing
    $hash = (Get-FileHash $path -Algorithm MD5).Hash.ToLowerInvariant()
    if ($hash -ne $md5) { throw "MD5 mismatch for $url (got $hash, expected $md5)" }
    $script:bytes += (Get-Item $path).Length
    Write-Host ("  {0,-60} {1,8:N0} KB" -f (Split-Path $path -Leaf), ((Get-Item $path).Length / 1KB))
}

Write-Host "HDRI"
$files = Invoke-RestMethod "$api$($hdri.id)"
$entry = $files.hdri.($hdri.res).hdr
Get-Checked $entry.url $entry.md5 (Join-Path $root "HDRI\$(Split-Path $entry.url -Leaf)")

Write-Host "Textures"
foreach ($t in $textures) {
    $files = Invoke-RestMethod "$api$($t.id)"
    foreach ($map in $t.maps) {
        $entry = $files.$map.($t.res).jpg
        Get-Checked $entry.url $entry.md5 (Join-Path $root "Textures\$($t.id)\$(Split-Path $entry.url -Leaf)")
    }
}

Write-Host "Models"
foreach ($m in $models) {
    $files = Invoke-RestMethod "$api$($m.id)"
    $gltf = $files.gltf.($m.res).gltf
    $folder = Join-Path $root "Models\$($m.id)"
    Get-Checked $gltf.url $gltf.md5 (Join-Path $folder (Split-Path $gltf.url -Leaf))
    foreach ($include in $gltf.include.PSObject.Properties) {
        Get-Checked $include.Value.url $include.Value.md5 (Join-Path $folder ($include.Name -replace '/', '\'))
    }
}

Write-Host ("Done: {0:N1} MB downloaded into {1}" -f ($script:bytes / 1MB), (Resolve-Path $root))

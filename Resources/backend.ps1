param (
    [Parameter(Mandatory)]
    [string]$Year,

    [Parameter(Mandatory)]
    [string]$Season,

    [Parameter(Mandatory)]
    [string]$Patch,

    [Parameter(Mandatory)]
    [string]$Username,

    [Parameter(Mandatory)]
    [int]$MaxDownloads,

    [string]$AppId = "359550",
    [string]$ManifestPath,
    [string]$DepotDownloader = "Resources\DepotDownloader.dll",
    [string]$OutputDir = "Downloads"
)

# -----------------------------
# Runtime configuration
# -----------------------------
$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

# -----------------------------
# Helpers
# -----------------------------
function Fail {
    param(
        [Parameter(Mandatory)][string]$Message,
        [int]$Code = 1
    )
    Write-Host "[ERR] $Message"
    exit $Code
}

# -----------------------------
# Load Manifest Json
# -----------------------------
if (!$ManifestPath -or !(Test-Path $ManifestPath)) {
    $ManifestPath = Join-Path $PSScriptRoot 'manifest.json'
}

if (!$DepotDownloader -or !(Test-Path $DepotDownloader)) {
    $DepotDownloader = Join-Path $PSScriptRoot 'DepotDownloader.dll'
}

if (!(Test-Path $ManifestPath)) {
    Fail "manifest.json not found at '$ManifestPath'." 2
}

if (!(Test-Path $DepotDownloader)) {
    Fail "DepotDownloader.dll not found at '$DepotDownloader'. Place it in your Resources folder so it copies next to backend.ps1." 3
}

try {
    $manifest = Get-Content $ManifestPath -Raw | ConvertFrom-Json
} catch {
    Fail ("Error reading manifest file: '{0}'. {1}" -f $ManifestPath, $_.Exception.Message) 4
}



# $manifest = Get-Content $ManifestPath -Raw | ConvertFrom-Json

# -----------------------------
# Validate Path
# -----------------------------
try {
    $patchData = $manifest.$Year.$Season.$Patch
    $seasonName = $manifest.$Year.$Season.name
} catch {
    Fail "Invalid Year / Season / Patch selection: $Year / $Season / $Patch" 5
}

# -----------------------------
# Prepare Output Folder
# -----------------------------

$hmPatch = "Y5.S4.2"
$downloadDirName = ($Year + $Season + '_' + $seasonName) -replace ' ', ''

if ($hmPatch -ieq "$Year.$Season.$Patch") {
    $downloadDirName = ($downloadDirName + "HM")
}

$downloadDir = Join-Path $OutputDir $downloadDirName
New-Item -ItemType Directory -Force -Path $downloadDir | Out-Null

# -----------------------------
# Download Function
# -----------------------------
function Invoke-DepotDownload {
    param (
        [string]$DepotId,
        [string]$ManifestId,
        [string]$AppId,
        [string]$Username,
        [int]$MaxDownloads
    )

    Write-Host "Attempting depot $DepotId with manifest $ManifestId"

    # Use full path to dotnet since script may run in a restricted PATH context
    $dotnetExe = "C:\Program Files\dotnet\dotnet.exe"
    if (!(Test-Path $dotnetExe)) {
        $dotnetExe = "dotnet"
    }

    # DepotDownloader needs to run from its own directory so it can find its DLL dependencies
    $depotDir = Split-Path $DepotDownloader -Parent
    Push-Location $depotDir
    try {
        & $dotnetExe (Split-Path $DepotDownloader -Leaf) `
            -app $AppId `
            -depot $DepotId `
            -manifest $ManifestId `
            -username $Username `
            -remember-password `
            -dir $downloadDir `
            -validate `
            -max-downloads $MaxDownloads

        if ($LASTEXITCODE -ne 0) {
            throw "DepotDownloader failed for depot $DepotId (exit code $LASTEXITCODE)."
        }
    } finally {
        Pop-Location
    }
}

# -----------------------------
# Iterate Depots (Fallback Logic)
# -----------------------------

foreach ($property in $patchData.PSObject.Properties) {
    if ($property.Name -match '^\d+$') {
        $depotId = $property.Name
        $manifestId = $property.Value

        Invoke-DepotDownload -AppId $AppId -DepotId $depotId -ManifestId $manifestId -Username $Username -MaxDownloads $MaxDownloads
  
    }
}


# -----------------------------
# Post Processing
# -----------------------------
[int]$yearInt = $Year.Substring(1)
[int]$seasonInt = $Season.Substring(1)
[int]$patchInt = $Patch

# $seasonString = $yearInt.$seasonInt.$patchInt
# helios path
$heliosPath = "$PSScriptRoot\HeliosLoader"
$throwbackPath = "$PSScriptRoot\ThrowbackLoader"
$throwbackloaderFiles = @{
    f1 = "defaultargs.dll";
    f2 = "LaunchR6.bat";
    f3 = "steam_api64.dll";
    f4 = "ThrowbackLoader.toml";
    f5 = "upc_r1_loader64.dll"; 
    f6 = "upc_r2_loader64.dll"; 
    f7 = "uplay_r1_loader64.dll" 
}

function Copy-IfExists {
    param([string[]]$Files)
    foreach ($f in $Files) {
        if (Test-Path $f) {
            Copy-Item $f -Destination $downloadDir -Force
        } else {
            Write-Host "[WARN] Missing file: $f"
        }
    }
}

$version = "$yearInt.$seasonInt.$patchInt"
switch -Wildcard ($version) {
    "5.4.2" {
        if (Test-Path $heliosPath) {
            Copy-Item (Join-Path $heliosPath "*") -Destination $downloadDir -Recurse -Force
        } else {
            Write-Host "[WARN] HeliosLoader folder not found at '$heliosPath'."
        }
    }
    "6.3.*" {
        Copy-IfExists @(
            (Join-Path $throwbackPath $throwbackloaderFiles.f1),
            (Join-Path $throwbackPath $throwbackloaderFiles.f2),
            (Join-Path $throwbackPath $throwbackloaderFiles.f3),
            (Join-Path $throwbackPath $throwbackloaderFiles.f4),
            (Join-Path $throwbackPath $throwbackloaderFiles.f5)
        )
    }
    default {
        Copy-IfExists @(
            (Join-Path $throwbackPath $throwbackloaderFiles.f1),
            (Join-Path $throwbackPath $throwbackloaderFiles.f2),
            (Join-Path $throwbackPath $throwbackloaderFiles.f3),
            (Join-Path $throwbackPath $throwbackloaderFiles.f4),
            (Join-Path $throwbackPath $throwbackloaderFiles.f7)
        )
    }
}


Write-Host "Download complete."

# Write ThrowbackLoader.toml with username
$tomlPath = Join-Path $downloadDir "ThrowbackLoader.toml"
if (!(Test-Path $tomlPath)) {
    $tomlContent = @"
[general]
username = '$Username' # edit to change in-game username. Limit to 16 characters

[advanced]
custom_user_id = '' # leave blank to use default
"@
    Set-Content -Path $tomlPath -Value $tomlContent -Encoding UTF8
    Write-Host "ThrowbackLoader.toml written for user '$Username'."
}

exit 0

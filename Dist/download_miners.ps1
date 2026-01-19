$ErrorActionPreference = "Stop"

# Use the current script's directory + "Miners" as the base directory
$baseDir = Join-Path $PSScriptRoot "Miners"

Write-Host "Miners Directory: $baseDir"

if (!(Test-Path $baseDir)) {
    New-Item -ItemType Directory -Force -Path $baseDir | Out-Null
    Write-Host "Created Miners directory."
}

# GitHub requires TLS 1.2
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12

function Download-Miner {
    param (
        [string]$Name,
        [string]$Repo,
        [string]$Filter
    )

    Write-Host "------------------------------------------------"
    Write-Host "Checking latest release for $Name ($Repo)..."
    
    try {
        $latestReleaseUrl = "https://api.github.com/repos/$Repo/releases/latest"
        $response = Invoke-RestMethod -Uri $latestReleaseUrl
        
        $asset = $response.assets | Where-Object { $_.name -match $Filter } | Select-Object -First 1
        
        if ($null -eq $asset) {
            Write-Error "Could not find a matching asset for $Name with filter '$Filter'"
            Write-Host "Available assets in latest release:"
            $response.assets | ForEach-Object { Write-Host " - $($_.name)" }
            return
        }

        $downloadUrl = $asset.browser_download_url
        $fileName = $asset.name
        $destPath = Join-Path $baseDir $fileName

        if (!(Test-Path $destPath)) {
            Write-Host "Downloading $fileName..."
            Invoke-WebRequest -Uri $downloadUrl -OutFile $destPath
            Write-Host "Download complete."
        }
        else {
            Write-Host "$fileName already exists. Skipping download."
        }

        Write-Host "Extracting $fileName..."
        try {
            Expand-Archive -Path $destPath -DestinationPath $baseDir -Force -ErrorAction Stop
            Write-Host "Extraction complete."
        }
        catch {
            Write-Warning "Extraction failed. You may need to extract $fileName manually."
            # Don't stop script execution for extraction failure
        }
        
    }
    catch {
        Write-Error "Failed to download $Name : $_"
    }
}

# Filters based on latest GitHub release naming conventions (as of Jan 2026)
Download-Miner -Name "XMRig" -Repo "xmrig/xmrig" -Filter "windows-x64.zip" 
Download-Miner -Name "Rigel" -Repo "rigelminer/rigel" -Filter "win.*\.zip"

Write-Host "------------------------------------------------"
Write-Host "All operations finished."
Write-Host "Please ensure your anti-virus did not quarantine the miner executables."
# Keep window open to see output
Read-Host -Prompt "Press Enter to exit"

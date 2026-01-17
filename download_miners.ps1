$ErrorActionPreference = "Stop"

$baseDir = "bin\Debug\net8.0-windows\Miners"
if (!(Test-Path $baseDir)) {
    New-Item -ItemType Directory -Force -Path $baseDir | Out-Null
}

function Download-Miner {
    param (
        [string]$Name,
        [string]$Repo,
        [string]$Filter
    )

    Write-Host "Checking latest release for $Name..."
    
    try {
        $latestReleaseUrl = "https://api.github.com/repos/$Repo/releases/latest"
        $response = Invoke-RestMethod -Uri $latestReleaseUrl
        
        $asset = $response.assets | Where-Object { $_.name -match $Filter } | Select-Object -First 1
        
        if ($null -eq $asset) {
            Write-Error "Could not find a matching asset for $Name with filter '$Filter'"
        }

        $downloadUrl = $asset.browser_download_url
        $fileName = $asset.name
        $destPath = Join-Path $baseDir $fileName

        if (!(Test-Path $destPath)) {
            Write-Host "Downloading $fileName from $downloadUrl..."
            Invoke-WebRequest -Uri $downloadUrl -OutFile $destPath
        } else {
            Write-Host "$fileName already exists. Skipping download."
        }

        Write-Host "Extracting $fileName..."
        Expand-Archive -Path $destPath -DestinationPath $baseDir -Force

        # Move executables to baseDir for easier access (Optional, but good for simple paths)
        $extractedRoot = Get-ChildItem -Path $baseDir -Directory | Where-Object { $_.Name -match $Name } | Select-Object -First 1 -ExpandProperty FullName
        if ($extractedRoot) {
            Write-Host "Found extracted folder: $extractedRoot"
            # Logic to find exe and move or just inform user
            $exe = Get-ChildItem -Path $extractedRoot -Filter "*.exe" -Recurse | Select-Object -First 1
            if ($exe) {
                Write-Host "Executable found at: $($exe.FullName)"
                # To simplify, we can copy the exe and config to the miners root, or just leave it.
                # Let's leave it and let the auto-config finder search recursively.
            }
        }
        
    } catch {
        Write-Error "Failed to download $Name : $_"
    }
}

Download-Miner -Name "XMRig" -Repo "xmrig/xmrig" -Filter "gcc-win64.zip"
Download-Miner -Name "Rigel" -Repo "rigelminer/rigel" -Filter "win-.*.zip"

Write-Host "Download and extraction complete. Miners are in $baseDir"

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

# Helper for JSON Parsing (PS 2.0 compatible)
function Parse-Json {
    param ($json)
    if (Get-Command ConvertFrom-Json -ErrorAction SilentlyContinue) {
        return $json | ConvertFrom-Json
    }
    else {
        Add-Type -AssemblyName System.Web.Extensions
        $serializer = New-Object System.Web.Script.Serialization.JavaScriptSerializer
        return $serializer.DeserializeObject($json)
    }
}

# Helper for Web Request (PS 2.0 compatible)
function Get-WebData {
    param ($Url)
    $webClient = New-Object System.Net.WebClient
    $webClient.Headers.Add("User-Agent", "PowerShell")
    try {
        return $webClient.DownloadString($Url)
    }
    finally {
        $webClient.Dispose()
    }
}

# Helper for File Download (PS 2.0 compatible)
function Download-File {
    param ($Url, $Dest)
    $webClient = New-Object System.Net.WebClient
    $webClient.Headers.Add("User-Agent", "PowerShell")
    try {
        $webClient.DownloadFile($Url, $Dest)
    }
    finally {
        $webClient.Dispose()
    }
}

# Helper for Extraction (Multi-version compatible)
function Extract-Zip {
    param ($ZipPath, $DestPath)

    Write-Host "Extracting $ZipPath..."

    # Method 1: Expand-Archive (PS 5.0+)
    if (Get-Command Expand-Archive -ErrorAction SilentlyContinue) {
        Expand-Archive -Path $ZipPath -DestinationPath $DestPath -Force
        Write-Host "Extraction complete (Expand-Archive)."
        return
    }

    # Method 2: .NET 4.5 System.IO.Compression.ZipFile
    try {
        Add-Type -AssemblyName System.IO.Compression.FileSystem
        $zip = [System.IO.Compression.ZipFile]::OpenRead($ZipPath)
        foreach ($entry in $zip.Entries) {
            $targetPath = Join-Path $DestPath $entry.FullName
            $targetDir = Split-Path $targetPath -Parent
            if (!(Test-Path $targetDir)) { New-Item -ItemType Directory -Force -Path $targetDir | Out-Null }

            # Manual Overwrite Check
            if (Test-Path $targetPath) { Remove-Item -Path $targetPath -Force }

            # Extension method call via static wrapper if possible or reflection, but PS usually handles methods on objects
            # $entry.ExtractToFile($targetPath)
            [System.IO.Compression.ZipFileExtensions]::ExtractToFile($entry, $targetPath)
        }
        $zip.Dispose()
        Write-Host "Extraction complete (.NET ZipFile)."
        return
    }
    catch {
        Write-Warning "NET extraction failed: $_"
    }

    # Method 3: Shell.Application (Legacy)
    try {
        $shell = New-Object -ComObject Shell.Application
        $zipFolder = $shell.NameSpace($ZipPath)
        $destFolder = $shell.NameSpace($DestPath)
        if ($null -ne $zipFolder -and $null -ne $destFolder) {
            # 16 = Respond with "Yes to All" for any dialog box that is displayed.
            # 4 = Do not display a progress dialog box.
            $destFolder.CopyHere($zipFolder.Items(), 20)
            Write-Host "Extraction complete (Shell.Application)."
            return
        }
    }
    catch {
        Write-Warning "Shell extraction failed: $_"
    }

    Write-Error "Could not extract $ZipPath. Please manually extract it to $DestPath"
}

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
        $json = Get-WebData -Url $latestReleaseUrl
        
        # Parse JSON
        $data = Parse-Json -json $json

        # Handle PSObject (ConvertFrom-Json) vs Dictionary (JavaScriptSerializer)
        $assets = $null
        if ($data -is [System.Collections.IDictionary]) {
            $assets = $data["assets"]
        } else {
            $assets = $data.assets
        }

        # Find Asset
        $matchingAsset = $null
        foreach ($asset in $assets) {
            $n = if ($asset -is [System.Collections.IDictionary]) { $asset["name"] } else { $asset.name }
            if ($n -match $Filter) {
                $matchingAsset = $asset
                break
            }
        }
        
        if ($null -eq $matchingAsset) {
            Write-Error "Could not find a matching asset for $Name with filter '$Filter'"
            return
        }

        $downloadUrl = if ($matchingAsset -is [System.Collections.IDictionary]) { $matchingAsset["browser_download_url"] } else { $matchingAsset.browser_download_url }
        $fileName = if ($matchingAsset -is [System.Collections.IDictionary]) { $matchingAsset["name"] } else { $matchingAsset.name }
        $destPath = Join-Path $baseDir $fileName

        if (!(Test-Path $destPath)) {
            Write-Host "Downloading $fileName..."
            Download-File -Url $downloadUrl -Dest $destPath
            Write-Host "Download complete."
        }
        else {
            Write-Host "$fileName already exists. Skipping download."
        }

        Extract-Zip -ZipPath $destPath -DestPath $baseDir
        
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
if ($Host.Name -eq "ConsoleHost") {
    Read-Host -Prompt "Press Enter to exit"
}

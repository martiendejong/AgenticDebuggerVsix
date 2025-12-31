<#
.SYNOPSIS
    Automates the publishing of the VSIX to the Visual Studio Marketplace.
    
.DESCRIPTION
    This script builds the solution in Release mode and then uses VsixPublisher.exe to publish the extension.
    You must provide a valid Personal Access Token (PAT).
    
.PARAMETER Pat
    The Personal Access Token for Visual Studio Marketplace.
    
.EXAMPLE
    .\Publish-Vsix.ps1 -Pat "your-personal-access-token"
#>

param(
    [Parameter(Mandatory=$true)]
    [string]$Pat,
    
    [string]$ManifestPath = "..\AgenticDebuggerVsix2\source.extension.vsixmanifest",
    [string]$VsixPath = "..\AgenticDebuggerVsix2\bin\Release\AgenticDebuggerVsix2.vsix"
)

# 1. Locate VsixPublisher.exe
$vsixPublisher = "C:\Program Files\Microsoft Visual Studio\2022\Enterprise\VSSDK\VisualStudioIntegration\Tools\Bin\VsixPublisher.exe"
if (-not (Test-Path $vsixPublisher)) {
    $vsixPublisher = "C:\Program Files\Microsoft Visual Studio\2022\Community\VSSDK\VisualStudioIntegration\Tools\Bin\VsixPublisher.exe"
}

if (-not (Test-Path $vsixPublisher)) {
    Write-Error "VsixPublisher.exe not found. Please install the VS SDK."
    exit 1
}

# 2. Build Solution (Release)
Write-Host "Building Solution (Release)..."
dotnet build ..\AgenticDebuggerVsix.sln -c Release

if ($LASTEXITCODE -ne 0) {
    Write-Error "Build failed."
    exit 1
}

# 3. Publish
Write-Host "Publishing VSIX..."
# Note: You need the 'Publisher' name from the manifest and the VSIX ID.
# VsixPublisher.exe publish -payload "path\to\vsix" -publishManifest "path\to\manifest" -personalAccessToken "token"

& $vsixPublisher publish -payload $VsixPath -publishManifest $ManifestPath -personalAccessToken $Pat

if ($LASTEXITCODE -eq 0) {
    Write-Host "Successfully published VSIX!" -ForegroundColor Green
} else {
    Write-Error "Publish failed."
}

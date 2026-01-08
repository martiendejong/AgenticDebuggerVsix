# IncrementVersion.ps1
# Auto-increments the build number in source.extension.vsixmanifest
# Version format: Major.Minor.Build (e.g., 1.3.0 -> 1.3.1)

param(
    [string]$ManifestPath = "$PSScriptRoot\source.extension.vsixmanifest"
)

if (-not (Test-Path $ManifestPath)) {
    Write-Error "Manifest not found: $ManifestPath"
    exit 1
}

$content = Get-Content $ManifestPath -Raw
$pattern = '(<Identity[^>]*Version=")([^"]+)(")'

if ($content -match $pattern) {
    $currentVersion = $Matches[2]
    $parts = $currentVersion.Split('.')

    # Ensure we have at least Major.Minor.Build format
    while ($parts.Count -lt 3) {
        $parts += "0"
    }

    # Increment build number
    $parts[2] = [int]$parts[2] + 1
    $newVersion = $parts -join '.'

    $newContent = $content -replace $pattern, "`${1}$newVersion`${3}"
    Set-Content $ManifestPath -Value $newContent -NoNewline

    Write-Host "Version updated: $currentVersion -> $newVersion"
} else {
    Write-Error "Could not find Version attribute in manifest"
    exit 1
}

# Builds a distributable .streamDeckPlugin archive from the compiled plugin output.
# Run this after building the project (Debug/Release) to generate the installer file.

[CmdletBinding()]
param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release"
)

$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$binRoot = Join-Path $scriptRoot "bin"
$configurationOutput = Join-Path $binRoot $Configuration
$pluginFolderName = "com.mhwlng.starcitizen.sdPlugin"
$sourcePath = Join-Path $configurationOutput $pluginFolderName
$zipPath = Join-Path $configurationOutput "com.mhwlng.starcitizen.zip"
$outputPath = Join-Path $configurationOutput "com.mhwlng.starcitizen.streamDeckPlugin"

New-Item -ItemType Directory -Force -Path $configurationOutput | Out-Null

if (-not (Test-Path $sourcePath)) {
    throw "Not found: $sourcePath. Build the project first to generate the plugin folder."
}

Remove-Item $zipPath, $outputPath -Force -ErrorAction SilentlyContinue

Compress-Archive -Path $sourcePath -DestinationPath $zipPath -Force
Move-Item $zipPath $outputPath -Force

Write-Host "Created: $outputPath"

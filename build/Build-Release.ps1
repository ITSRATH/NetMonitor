# Builds the release package dist\NetMonitor-<version>.zip
#   powershell -ExecutionPolicy Bypass -File build\Build-Release.ps1
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$version = [regex]::Match((Get-Content (Join-Path $root 'src\Core.cs') -Raw), 'Version\s*=\s*"([^"]+)"').Groups[1].Value
if (-not $version) { throw 'Version not found in src\Core.cs' }

# Compile check before packaging
$refs = 'System.Windows.Forms', 'System.Drawing', 'System.Net.Http', 'System.Web.Extensions', 'System.IO.Compression'
Add-Type -AssemblyName $refs
Add-Type -Path (Get-ChildItem (Join-Path $root 'src') -Filter '*.cs').FullName -ReferencedAssemblies $refs -WarningAction SilentlyContinue

$dist = Join-Path $root 'dist'
$stage = Join-Path $dist "NetMonitor-$version"
if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }
New-Item -ItemType Directory -Force (Join-Path $stage 'src') | Out-Null
foreach ($f in 'NetMonitor.ps1', 'NetMonitor.cmd', 'Setup.cmd', 'README.md', 'LICENSE') { Copy-Item (Join-Path $root $f) $stage }
Copy-Item (Join-Path $root 'src\*.cs') (Join-Path $stage 'src')

$zip = Join-Path $dist "NetMonitor-$version.zip"
if (Test-Path $zip) { Remove-Item $zip -Force }
Add-Type -AssemblyName System.IO.Compression, System.IO.Compression.FileSystem
$archive = [IO.Compression.ZipFile]::Open($zip, 'Create')
try {
    foreach ($file in Get-ChildItem $stage -Recurse -File) {
        # Einträge mit "/" (ZIP-Standard), damit alle Entpacker die Ordner korrekt anlegen
        $entry = "NetMonitor-$version/" + $file.FullName.Substring($stage.Length + 1).Replace('\', '/')
        [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive, $file.FullName, $entry, 'Optimal') | Out-Null
    }
} finally { $archive.Dispose() }
Remove-Item $stage -Recurse -Force
Write-Host "Release package: $zip"

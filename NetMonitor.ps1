# NetMonitor launcher.
# Compiles the C# sources in .\src at start-up (works with Windows Smart App Control, no unsigned .exe needed).
#   (no parameters)   start NetMonitor
#   -Setup            run the installer
#   -Uninstall        run the uninstaller
#   -CapturePids ...  elevated helper for game server detection (started by NetMonitor itself)
param([switch]$Setup, [switch]$Uninstall, [string]$CapturePids, [int]$Seconds = 12, [string]$Out)

$ErrorActionPreference = 'Stop'
$refs = 'System.Windows.Forms', 'System.Drawing'
Add-Type -AssemblyName $refs
try {
    $sources = (Get-ChildItem -Path (Join-Path $PSScriptRoot 'src') -Filter '*.cs').FullName
    Add-Type -Path $sources -ReferencedAssemblies $refs -WarningAction SilentlyContinue
    if ($Out) {
        [NetMonitor.Detector]::Capture([int[]]($CapturePids -split ','), $Seconds, $Out)
    } elseif ($Setup) {
        [NetMonitor.Program]::Setup($PSCommandPath)
    } elseif ($Uninstall) {
        [NetMonitor.Program]::Uninstall($PSCommandPath)
    } else {
        [NetMonitor.Program]::Run($PSCommandPath)
    }
} catch {
    if ($Out) {
        [IO.File]::WriteAllText($Out, "error;$($_.Exception.Message)")
    } else {
        [System.Windows.Forms.MessageBox]::Show("NetMonitor could not be started:`n`n$($_.Exception.Message)",
            'NetMonitor', 'OK', 'Error') | Out-Null
    }
}

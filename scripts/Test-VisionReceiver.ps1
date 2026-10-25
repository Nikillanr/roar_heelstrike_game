# Network/source checks with minimal Unity API stubs, not a Unity Play-mode test.
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$sources = @(
    (Join-Path $projectRoot 'scripts/vision-tests/ReceiverHarness.cs'),
    (Join-Path $projectRoot 'Shared/com.mtpad.rehab.core/Runtime/RehabClock.cs'),
    (Join-Path $projectRoot 'HeadsetUnity/Assets/Rehab/Vision/VisionStateReceiver.cs')
)
Add-Type -Path $sources -ReferencedAssemblies @('System.dll', 'System.Core.dll', 'System.Web.Extensions.dll') -WarningAction SilentlyContinue
[ReceiverHarness]::Run()

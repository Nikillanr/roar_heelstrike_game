param(
    [string]$UnityEditorPath = ''
)

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$expectedVersion = '2022.3.30f1'
$missing = 0

foreach ($project in @('TriggerUnity', 'HeadsetUnity')) {
    $folder = Join-Path $projectRoot $project
    $version = Get-Content (Join-Path $folder 'ProjectSettings/ProjectVersion.txt') -Raw
    if ($version -notmatch "m_EditorVersion: $([regex]::Escape($expectedVersion))(\r?\n|$)") {
        throw "$project does not specify Unity $expectedVersion."
    }
    $manifest = Get-Content (Join-Path $folder 'Packages/manifest.json') -Raw | ConvertFrom-Json
    $null = Get-Content (Join-Path $folder 'Packages/packages-lock.json') -Raw | ConvertFrom-Json
    foreach ($dependency in $manifest.dependencies.PSObject.Properties) {
        if ($dependency.Value.StartsWith('file:')) {
            $target = [IO.Path]::GetFullPath((Join-Path (Join-Path $folder 'Packages') $dependency.Value.Substring(5)))
            if (!(Test-Path -LiteralPath $target)) { throw "Missing local dependency: $target" }
            Write-Output "OK $project local package: $($dependency.Name)"
        }
    }
    $scene = if ($project -eq 'TriggerUnity') { 'Trigger_Main' } else { 'Headset_Main' }
    if (!(Test-Path (Join-Path $folder "Assets/Scenes/$scene.unity"))) { throw "Missing scene: $scene" }
    Write-Output "OK $project version, manifests, and main scene"
}

if (!$UnityEditorPath) {
    $candidates = @(
        "$env:ProgramFiles\Unity\Hub\Editor\$expectedVersion\Editor\Unity.exe",
        "$env:ProgramFiles\Unity $expectedVersion\Editor\Unity.exe"
    )
    $registryPath = Join-Path $env:APPDATA 'UnityHub/editors-v2.json'
    if (Test-Path $registryPath) {
        $registry = Get-Content $registryPath -Raw | ConvertFrom-Json
        foreach ($entry in @($registry.data)) {
            if ($entry.version -eq $expectedVersion -and $entry.location) { $candidates += $entry.location }
        }
    }
    $UnityEditorPath = $candidates | Where-Object { Test-Path -LiteralPath $_ -PathType Leaf } | Select-Object -First 1
}

if ($UnityEditorPath -and (Test-Path -LiteralPath $UnityEditorPath -PathType Leaf)) {
    $versionInfo = (Get-Item -LiteralPath $UnityEditorPath).VersionInfo
    if ($versionInfo.ProductVersion -notlike "$expectedVersion*") {
        Write-Output "MISSING matching editor: supplied binary reports $($versionInfo.ProductVersion)"
        $missing++
    } else {
        Write-Output "OK Unity editor: $UnityEditorPath"
    }
    $android = Join-Path (Split-Path -Parent $UnityEditorPath) 'Data/PlaybackEngines/AndroidPlayer'
    foreach ($module in @('SDK/platform-tools/adb.exe', 'NDK/source.properties', 'OpenJDK/bin/java.exe')) {
        if (Test-Path (Join-Path $android $module)) { Write-Output "OK Android tool: $module" }
        else { Write-Output "OPTIONAL for editor-only work; missing Android tool: $module" }
    }
} else {
    Write-Output "MISSING Unity $expectedVersion. Install through Hub or pass -UnityEditorPath."
    $missing++
}

Write-Output 'Hardware is not required for editing. Compilation and Play mode require Unity; this check does not run them.'
if ($missing -gt 0) { exit 1 }

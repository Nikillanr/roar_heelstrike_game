# Runs actual Unity API checks in a small isolated project. Leaves the user's open project alone.
param([string]$UnityEditorPath = 'C:\Program Files\Unity\Hub\Editor\2022.3.30f1\Editor\Unity.exe', [switch]$NetworkBenchmark, [switch]$LiveCameraBenchmark)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$checkRoot = Join-Path $projectRoot '.tools/UnityVisionChecks'
foreach ($folder in @('Assets/Editor', 'Packages', 'ProjectSettings')) {
    New-Item -ItemType Directory -Force (Join-Path $checkRoot $folder) | Out-Null
}
foreach ($file in @('VisionStateReceiver.cs', 'VisionBallController.cs', 'VisionDesktopDemo.cs')) {
    Copy-Item -LiteralPath (Join-Path $projectRoot "HeadsetUnity/Assets/Rehab/Vision/$file") -Destination (Join-Path $checkRoot "Assets/$file") -Force
}
Copy-Item -LiteralPath (Join-Path $projectRoot 'Shared/com.mtpad.rehab.core/Runtime/RehabClock.cs') -Destination (Join-Path $checkRoot 'Assets/RehabClock.cs') -Force
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'vision-tests/VisionUnityChecks.cs') -Destination (Join-Path $checkRoot 'Assets/Editor/VisionUnityChecks.cs') -Force
[IO.File]::WriteAllText((Join-Path $checkRoot 'ProjectSettings/ProjectVersion.txt'), "m_EditorVersion: 2022.3.30f1`n")
[IO.File]::WriteAllText((Join-Path $checkRoot 'Packages/manifest.json'), '{"dependencies":{"com.unity.modules.imgui":"1.0.0","com.unity.modules.jsonserialize":"1.0.0","com.unity.modules.physics":"1.0.0"}}')
$logPath = Join-Path $checkRoot 'checks.log'
$arguments = @('-batchmode', '-nographics', '-projectPath', ('"' + $checkRoot + '"'),
    '-executeMethod', 'VisionUnityChecks.Run', '-logFile', ('"' + $logPath + '"'))
if ($NetworkBenchmark -or $LiveCameraBenchmark) {
    $arguments[5] = 'VisionUnityChecks.RunNetworkBenchmark'
    if ($LiveCameraBenchmark) { $arguments[5] = 'VisionUnityChecks.RunLiveBenchmark' }
    if (Test-Path $logPath) { Remove-Item -LiteralPath $logPath }
    $process = Start-Process -FilePath $UnityEditorPath -ArgumentList $arguments -PassThru -WindowStyle Hidden
    try {
        $deadline = (Get-Date).AddSeconds(45)
        while (!(Test-Path $logPath) -or !(Select-String -LiteralPath $logPath -Pattern 'VISION_NETWORK_READY')) {
            if ($process.HasExited -or (Get-Date) -gt $deadline) { throw "Benchmark Unity did not start. See $logPath" }
            Start-Sleep -Milliseconds 250
        }
        & (Join-Path $projectRoot '.tools/python311/python.exe') (Join-Path $projectRoot 'VisionInput/benchmark_latency.py') --stage unity --port 17778
        if ($LASTEXITCODE -ne 0) { throw 'Unity latency benchmark failed' }
        if ($LiveCameraBenchmark) {
            foreach ($inferenceMode in @('video', 'image')) {
                & (Join-Path $PSScriptRoot 'Run-Vision.ps1') -Mode webcam -Port 17778 -MeasureSeconds 8 -NoPreview -HoldSeconds 0.05 -InferenceMode $inferenceMode -ReportPath (Join-Path $checkRoot ("live-$inferenceMode.json"))
                if ($LASTEXITCODE -ne 0) { throw "Live $inferenceMode benchmark failed" }
            }
        }
        $process.WaitForExit(20000) | Out-Null
    } finally {
        if (!$process.HasExited) { Stop-Process -Id $process.Id }
    }
    exit 0
}
$process = Start-Process -FilePath $UnityEditorPath -ArgumentList $arguments -Wait -PassThru -WindowStyle Hidden
$result = $process.ExitCode
if (Test-Path $logPath) { Get-Content $logPath | Select-String 'VISION_|error CS|Exception' | ForEach-Object Line }
if ($result -ne 0) { throw "Unity vision checks failed. See $logPath" }
if (!(Select-String -LiteralPath $logPath -Pattern 'VISION_UNITY_CHECKS_OK')) { throw "Unity did not complete the checks. See $logPath" }

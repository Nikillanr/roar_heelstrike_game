param(
    [ValidateSet('manual', 'webcam', 'tests', 'benchmark')][string]$Mode = 'manual',
    [string]$HostName = '127.0.0.1',
    [int]$Port = 7778,
    [int]$CameraIndex = 0,
    [ValidateSet('left', 'right', 'either')][string]$Hand = 'either',
    [double]$HoldSeconds = 0,
    [ValidateSet('video', 'image')][string]$InferenceMode = 'video',
    [ValidateSet('any', 'msmf', 'dshow')][string]$CameraBackend = 'any',
    [ValidateSet('all', 'transport', 'model', 'camera', 'unity')][string]$BenchmarkStage = 'unity',
    [double]$MeasureSeconds = 0,
    [switch]$NoPreview,
    [string]$ReportPath = '',
    [string]$PythonPath = ''
)

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
if (!$PythonPath) {
    $PythonPath = Join-Path $projectRoot '.tools/python311/python.exe'
    if (!(Test-Path $PythonPath)) {
        $PythonPath = Join-Path $projectRoot 'VisionInput/.venv/Scripts/python.exe'
    }
    if (!(Test-Path $PythonPath)) { throw 'Python environment missing. Run scripts/Setup-Vision.ps1 first.' }
}
$PythonPath = (Resolve-Path -LiteralPath $PythonPath).Path
$env:MPLCONFIGDIR = Join-Path $projectRoot '.tools/matplotlib'
if (!$ReportPath) {
    $ReportPath = Join-Path $projectRoot ('.tools/latency/' + (Get-Date -Format 'yyyyMMdd-HHmmss') + '-' + $Mode + '.json')
} else { $ReportPath = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($ReportPath) }
Push-Location (Join-Path $projectRoot 'VisionInput')
try {
    if ($Mode -eq 'tests') { & $PythonPath -m unittest discover -s tests -v }
    elseif ($Mode -eq 'manual') { & $PythonPath manual_sender.py --host $HostName --port $Port }
    elseif ($Mode -eq 'benchmark') {
        & $PythonPath benchmark_latency.py --stage $BenchmarkStage --host $HostName --port $Port --camera $CameraIndex --output $ReportPath
    }
    else {
        $previewArguments = @()
        if ($NoPreview) { $previewArguments += '--no-preview' }
        & $PythonPath webcam_sender.py --host $HostName --port $Port --camera $CameraIndex --hand $Hand --hold $HoldSeconds --inference-mode $InferenceMode --backend $CameraBackend --seconds $MeasureSeconds --report $ReportPath @previewArguments
    }
    $result = $LASTEXITCODE
} finally { Pop-Location }
exit $result

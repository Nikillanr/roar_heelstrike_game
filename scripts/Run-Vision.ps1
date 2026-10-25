param(
    [ValidateSet('manual', 'webcam', 'tests')][string]$Mode = 'manual',
    [string]$HostName = '127.0.0.1',
    [int]$Port = 7778,
    [int]$CameraIndex = 0,
    [ValidateSet('left', 'right', 'either')][string]$Hand = 'either',
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
Push-Location (Join-Path $projectRoot 'VisionInput')
try {
    if ($Mode -eq 'tests') { & $PythonPath -m unittest discover -s tests -v }
    elseif ($Mode -eq 'manual') { & $PythonPath manual_sender.py --host $HostName --port $Port }
    else { & $PythonPath webcam_sender.py --host $HostName --port $Port --camera $CameraIndex --hand $Hand }
    $result = $LASTEXITCODE
} finally { Pop-Location }
exit $result

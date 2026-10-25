# Downloads a project-local Windows x64 runtime, dependencies, and pose model.
# Does not register a Python installation or edit PATH.
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$toolsPath = Join-Path $projectRoot '.tools'
$runtimePath = Join-Path $toolsPath 'python311'
$pythonPath = Join-Path $runtimePath 'python.exe'
New-Item -ItemType Directory -Force $toolsPath | Out-Null
if (!(Test-Path $pythonPath)) {
    $archivePath = Join-Path $toolsPath 'python-embed.zip'
    Invoke-WebRequest -UseBasicParsing 'https://www.python.org/ftp/python/3.11.9/python-3.11.9-embed-amd64.zip' -OutFile $archivePath
    Expand-Archive -LiteralPath $archivePath -DestinationPath $runtimePath -Force
}
[IO.File]::WriteAllText((Join-Path $runtimePath 'python311._pth'), "python311.zip`n.`nLib/site-packages`n../../VisionInput`nimport site`n")
if (!(Test-Path (Join-Path $runtimePath 'Lib/site-packages/pip/__main__.py'))) {
    $bootstrapPath = Join-Path $toolsPath 'get-pip.py'
    Invoke-WebRequest -UseBasicParsing 'https://bootstrap.pypa.io/get-pip.py' -OutFile $bootstrapPath
    & $pythonPath $bootstrapPath --disable-pip-version-check --no-warn-script-location
    if ($LASTEXITCODE -ne 0) { throw 'pip setup failed.' }
}
$requirements = Join-Path $projectRoot 'VisionInput/requirements.txt'
$locked = Join-Path $projectRoot 'VisionInput/requirements-lock.txt'
if (Test-Path $locked) { $requirements = $locked }
& $pythonPath -m pip install --disable-pip-version-check --no-warn-script-location -r $requirements
if ($LASTEXITCODE -ne 0) { throw 'Vision dependencies could not be installed.' }
$env:MPLCONFIGDIR = Join-Path $toolsPath 'matplotlib'
& $pythonPath (Join-Path $projectRoot 'VisionInput/download_model.py')
if ($LASTEXITCODE -ne 0) { throw 'Model download failed.' }
Write-Output 'Ready. Start the Unity desktop demo, then run scripts/Run-Vision.ps1 -Mode manual or -Mode webcam.'

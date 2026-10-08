# Прогон без игры: вместо бенчмарка запускается tests/FakeBenchmark.
# Занимает ~3 минуты, мышь и клавиатуру в это время не трогать.
param([string]$WorkDir = (Join-Path $env:TEMP 'wukong-fake-e2e'))

$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
$install = Join-Path $WorkDir 'FakeInstall'
$binDir = Join-Path $install 'b1\Binaries\Win64'
$configDir = Join-Path $install 'b1\Saved\Config\Windows'
$config = Join-Path $configDir 'GameUserSettings.ini'

Remove-Item $WorkDir -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force $binDir, $configDir | Out-Null
Copy-Item (Join-Path $repo 'tests\WukongBenchAutomator.Tests\TestData\GameUserSettings.ini') $config
$originalHash = (Get-FileHash $config).Hash

dotnet publish (Join-Path $repo 'tests\FakeBenchmark\FakeBenchmark.csproj') -c Release -o $binDir -nologo -v q
if ($LASTEXITCODE -ne 0) { throw 'FakeBenchmark build failed' }
dotnet build (Join-Path $repo 'src\WukongBenchAutomator\WukongBenchAutomator.csproj') -c Release -nologo -v q
if ($LASTEXITCODE -ne 0) { throw 'WukongBenchAutomator build failed' }

$env:FAKE_B1_LAYOUT = 'shifted'
$env:FAKE_B1_WEAK_GPU = '1'
$exe = Join-Path $repo 'src\WukongBenchAutomator\bin\Release\net8.0-windows10.0.19041.0\WukongBenchAutomator.exe'
& $exe --game-dir $install --launch direct --menu-delay 3 --timeout 4 --runs 2 --output (Join-Path $WorkDir 'results')
$exitCode = $LASTEXITCODE
Remove-Item Env:FAKE_B1_LAYOUT, Env:FAKE_B1_WEAK_GPU

$reportFile = Get-ChildItem (Join-Path $WorkDir 'results') -Recurse -Filter report.json | Select-Object -First 1
$report = Get-Content $reportFile.FullName -Raw -Encoding UTF8 | ConvertFrom-Json
$runDir = $reportFile.DirectoryName
$cpu = $report.Passes | Where-Object { $_.Profile.Kind -eq 'Cpu' }
$gpu = $report.Passes | Where-Object { $_.Profile.Kind -eq 'Gpu' }

$checks = [ordered]@{
    'exit code 0'                       = $exitCode -eq 0
    'config restored byte-for-byte'     = (Get-FileHash $config).Hash -eq $originalHash
    'backup cleaned up'                 = -not (Test-Path "$config.wukongbench-backup")
    'buttons found by text (OCR)'       = ($report.Passes.Notes -join ' ') -match 'OCR'
    'adaptive CPU test used 25% scale'  = $cpu.Profile.RenderScalePercent -eq 25 -and (($cpu.Notes -join ' ') -match 'Автоподстройка')
    'CPU test valid after adaptation'   = $cpu.Validity.Status -eq 'Valid'
    'GPU test valid'                    = $gpu.Validity.Status -eq 'Valid'
    '2 runs per pass with statistics'   = $cpu.Runs.Count -eq 2 -and $gpu.Runs.Count -eq 2 -and $null -ne $gpu.RunStatistics
    'telemetry collected'               = $null -ne $gpu.Telemetry -and $gpu.Telemetry.Samples -gt 3
    'results screenshots saved'         = Test-Path (Join-Path $runDir 'gpu\results_run1.png')
    'frames + telemetry CSV written'    = (Test-Path (Join-Path $runDir 'gpu\frames_run2.csv')) -and (Test-Path (Join-Path $runDir 'gpu\telemetry_run1.csv'))
    'verdict present'                   = $report.Verdict.Count -gt 0
}

Write-Host ''
$failed = 0
foreach ($check in $checks.GetEnumerator()) {
    $mark = if ($check.Value) { 'ok  ' } else { $failed++; 'FAIL' }
    Write-Host ("[{0}] {1}" -f $mark, $check.Key)
}

Write-Host "report: $($reportFile.FullName)"
if ($failed -gt 0) { Write-Host "E2E: FAILED ($failed)"; exit 1 }
Write-Host 'E2E: PASSED'

# Синтетические samples/cpu_sample.json и gpu_sample.json для --report. Это не реальные измерения.
param([string]$OutDir = (Join-Path $PSScriptRoot '..\samples'))

$ErrorActionPreference = 'Stop'
$inv = [Globalization.CultureInfo]::InvariantCulture
New-Item -ItemType Directory -Force $OutDir | Out-Null

function New-Sample {
    param(
        [string]$Path, [int]$Seed, [double]$CpuMs, [double]$GpuMs, [double]$Amplitude,
        [string]$Resolution, [int]$Quality, [int]$ImageQuality, [int]$Rtx, [double]$VramMb
    )

    $rand = [Random]::new($Seed)
    $duration = 146000.0
    $t = 0.0
    $records = [Collections.Generic.List[string]]::new()
    $frameTimes = [Collections.Generic.List[double]]::new()
    $cpuUsageSum = 0.0; $gpuUsageSum = 0.0

    while ($t -lt $duration) {
        # нагрузка меняется по сцене, плюс шум и редкие фризы
        $scene = 1 + $Amplitude * [Math]::Sin($t / 9000.0) + 0.5 * $Amplitude * [Math]::Sin($t / 2300.0)
        $cpu = $CpuMs * $scene * (0.94 + 0.12 * $rand.NextDouble())
        $gpu = $GpuMs * $scene * (0.95 + 0.10 * $rand.NextDouble())
        if ($rand.NextDouble() -lt 0.002) { $cpu += 12 + 20 * $rand.NextDouble() }
        $ft = [Math]::Max($cpu, $gpu) + 0.3
        $t += $ft
        $frameTimes.Add($ft)

        $cpuUsage = [Math]::Min(100, 100 * $cpu / $ft * 0.55)
        $gpuUsage = [Math]::Min(99.5, 100 * $gpu / $ft)
        $cpuUsageSum += $cpuUsage; $gpuUsageSum += $gpuUsage
        $vram = $VramMb + 300 * $rand.NextDouble()
        $records.Add([string]::Format($inv,
            '{{"FrameRate":{0:0.00},"CPUUsage":{1:0.0},"GPUUsage":{2:0.0},"CPUFrameTime":{3:0.000},"GPUFrameTime":{4:0.000},"VideoMemoryUsage":{5:0}}}',
            1000 / $ft, $cpuUsage, $gpuUsage, $cpu, $gpu, $vram))
    }

    $n = $frameTimes.Count
    $avg = $n / ($t / 1000)
    $sorted = $frameTimes.ToArray(); [Array]::Sort($sorted)
    $fps95 = 1000 / $sorted[[int][Math]::Ceiling(0.95 * $n) - 1]

    # мин/макс по секундным окнам
    $perSecond = [Collections.Generic.List[double]]::new()
    $acc = 0.0; $frames = 0
    foreach ($ft in $frameTimes) {
        $acc += $ft; $frames++
        if ($acc -ge 1000) { $perSecond.Add($frames * 1000 / $acc); $acc = 0; $frames = 0 }
    }

    $summary = [ordered]@{
        _note = 'SYNTHETIC SAMPLE for --report demo, not a real measurement'
        FPSAvg = [Math]::Round($avg, 1)
        FPSMax = [Math]::Round(($perSecond | Measure-Object -Maximum).Maximum, 1)
        FPSMin = [Math]::Round(($perSecond | Measure-Object -Minimum).Minimum, 1)
        FPS95 = [Math]::Round($fps95, 1)
        CPUAvg = [Math]::Round($cpuUsageSum / $n, 1)
        GPUAvg = [Math]::Round($gpuUsageSum / $n, 1)
        VideoMem = [Math]::Round(($VramMb + 300) / 1024, 1)
        GameVer = '1.0.8.14860'
        SysVer = 'Windows 11 Pro 23H2'
        CPUModel = 'AMD Ryzen 7 7800X3D 8-Core Processor'
        GPUModel = 'NVIDIA GeForce RTX 4070'
        GpuDriverVer = '566.36'
        VideoMemSize = '12 GB'
        SysMem = '32 GB'
        ScreenMode = 1
        ScreenResolution = $Resolution
        QualityLevel = $Quality
        ImageQuality = $ImageQuality
        ViewDistance = $Quality
        Rtx = $Rtx
        Dlss = 0
        InsertFrame = 0
        Dx12 = 1
    }

    $head = ($summary | ConvertTo-Json -Compress).TrimEnd('}')
    $json = $head + ',"Records":[' + ($records -join ',') + ']}'
    [IO.File]::WriteAllText($Path, $json, [Text.UTF8Encoding]::new($false))
    Write-Host ("{0}: {1} frames, avg FPS {2:0.0}" -f (Split-Path $Path -Leaf), $n, $avg)
}

# Бенчмарк пишет разрешение как "1920 × 1080" (знак умножения U+00D7)
$x = [char]0x00D7
New-Sample -Path (Join-Path $OutDir 'cpu_sample.json') -Seed 11 -CpuMs 7.6 -GpuMs 3.1 -Amplitude 0.18 `
    -Resolution "1280 $x 720" -Quality 1 -ImageQuality 50 -Rtx 0 -VramMb 4300
New-Sample -Path (Join-Path $OutDir 'gpu_sample.json') -Seed 23 -CpuMs 8.4 -GpuMs 27.5 -Amplitude 0.16 `
    -Resolution "2560 $x 1440" -Quality 5 -ImageQuality 100 -Rtx 4 -VramMb 10100

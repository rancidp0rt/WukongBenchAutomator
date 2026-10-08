# Black Myth: Wukong Benchmark Tool - отчёт

2026-10-08 19:57 · отчёт по готовым файлам результата · Wukong Bench Automator 1.0.0

## Вердикт

- • CPU: 125 FPS, 1% low 99.
- • GPU: 36 FPS, 1% low 29 (2560x1440, кинематографическое, RT).
- • На максимальных настройках 30-60 FPS, для 60 понадобится апскейл.
- • На максимальных настройках упираемся в GPU: CPU выдаёт примерно в 3.5 раза больше кадров (до 125 FPS).
- ✅ CPU-тест: ок, 100% кадров упираются в CPU.
- ✅ GPU-тест: ок, 100% кадров упираются в GPU.

## Характеристики ПК

| Компонент | Значение |
|---|---|
| ОС | Windows 11 Pro 23H2 |
| Процессор | AMD Ryzen 7 7800X3D 8-Core Processor |
| Видеокарта | NVIDIA GeForce RTX 4070 (12 GB) |
| Оперативная память | 32 GB |
| Драйвер GPU (по данным бенчмарка) | 566.36 |
| Версия Benchmark Tool | 1.0.8.14860 (DirectX 12) |

## Результаты

| Метрика | CPU-тест | GPU-тест |
|---|---:|---:|
| Средний FPS | 125.3 | 35.9 |
| Минимальный FPS | 101.3 | 29.5 |
| Максимальный FPS | 169.2 | 47.5 |
| 5% low (FPS95 из бенчмарка) | 103.2 | 29.9 |
| 1% low * | 98.9 | 28.9 |
| 0.1% low * | 30.0 | 28.1 |
| Медианный FPS * | 128.0 | 36.4 |
| Среднее время кадра, мс * | 7.98 | 27.87 |
| 99-й перцентиль времени кадра, мс * | 10.11 | 34.63 |
| CPU-время кадра (среднее), мс * | 7.68 | 8.47 |
| GPU-время кадра (среднее), мс * | 3.11 | 27.56 |
| Кадров, ограниченных CPU * | 100% | 0% |
| Кадров, ограниченных GPU * | 0% | 100% |
| Загрузка CPU (средняя), % | 53 | 17 |
| Загрузка GPU (средняя), % | 39 | 99 |
| Видеопамять, ГБ | 4.5 | 10.2 |
| Фризы (кадр дольше 2× медианы) * | 47 (19.3/мин) | 0 (0.0/мин) |
| Кадров в записи / длительность * | 18301 / 146 с | 5239 / 146 с |

* посчитано инструментом по покадровым записям бенчмарка (Records); перцентили - по времени кадра.

## Настройки

| Параметр | CPU-тест | GPU-тест |
|---|---:|---:|
| Разрешение экрана | 1280x720 | 2560x1440 |
| Масштаб рендера | 50% (рендер 640x360) | 100% (рендер 2560x1440) |
| Пресет качества | Низкое | Кинематографическое |
| Полная трассировка лучей | выкл | вкл, уровень 'сверхвысокий' |
| Генерация кадров | выкл | выкл |
| Апскейлер (SuperResolutionSampling) | значение 0 (по файлу результата) | значение 0 (по файлу результата) |

<details><summary>Все поля файла результата - CPU-тест (<code>cpu_sample.json</code>)</summary>

| Поле | Значение |
|---|---|
| _note | SYNTHETIC SAMPLE for --report demo, not a real measurement |
| FPSAvg | 125.3 |
| FPSMax | 169.2 |
| FPSMin | 101.3 |
| FPS95 | 103.2 |
| CPUAvg | 52.9 |
| GPUAvg | 39.2 |
| VideoMem | 4.5 |
| GameVer | 1.0.8.14860 |
| SysVer | Windows 11 Pro 23H2 |
| CPUModel | AMD Ryzen 7 7800X3D 8-Core Processor |
| GPUModel | NVIDIA GeForce RTX 4070 |
| GpuDriverVer | 566.36 |
| VideoMemSize | 12 GB |
| SysMem | 32 GB |
| ScreenMode | 1 |
| ScreenResolution | 1280 × 720 |
| QualityLevel | 1 |
| ImageQuality | 50 |
| ViewDistance | 1 |
| Rtx | 0 |
| Dlss | 0 |
| InsertFrame | 0 |
| Dx12 | 1 |

</details>

<details><summary>Все поля файла результата - GPU-тест (<code>gpu_sample.json</code>)</summary>

| Поле | Значение |
|---|---|
| _note | SYNTHETIC SAMPLE for --report demo, not a real measurement |
| FPSAvg | 35.9 |
| FPSMax | 47.5 |
| FPSMin | 29.5 |
| FPS95 | 29.9 |
| CPUAvg | 16.7 |
| GPUAvg | 98.9 |
| VideoMem | 10.2 |
| GameVer | 1.0.8.14860 |
| SysVer | Windows 11 Pro 23H2 |
| CPUModel | AMD Ryzen 7 7800X3D 8-Core Processor |
| GPUModel | NVIDIA GeForce RTX 4070 |
| GpuDriverVer | 566.36 |
| VideoMemSize | 12 GB |
| SysMem | 32 GB |
| ScreenMode | 1 |
| ScreenResolution | 2560 × 1440 |
| QualityLevel | 5 |
| ImageQuality | 100 |
| ViewDistance | 5 |
| Rtx | 4 |
| Dlss | 0 |
| InsertFrame | 0 |
| Dx12 | 1 |

</details>


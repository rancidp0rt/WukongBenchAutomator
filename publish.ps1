# Собирает self-contained single-file exe: publish\WukongBenchAutomator.exe (.NET на целевом ПК не нужен)
$ErrorActionPreference = 'Stop'
dotnet publish "$PSScriptRoot\src\WukongBenchAutomator\WukongBenchAutomator.csproj" -c Release -r win-x64 -o "$PSScriptRoot\publish" -nologo
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
Get-Item "$PSScriptRoot\publish\WukongBenchAutomator.exe" | Select-Object FullName, @{ n = 'SizeMB'; e = { [math]::Round($_.Length / 1MB, 1) } }

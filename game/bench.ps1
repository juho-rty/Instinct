# 재기: 창을 띄워 최고 속도로 흘리며 틀마다 걸린 시간을 찍는다.  예) .\bench.ps1 3 20 300
param([int]$seed = 3, [double]$seconds = 20, [int]$skip = 0, [string]$hide = "")
$dn = "C:\Program Files\dotnet\dotnet.exe"
$godot = Get-ChildItem "$env:LOCALAPPDATA\Microsoft\WinGet\Packages\GodotEngine.GodotEngine.Mono*" -Recurse -Filter "Godot_v*_mono_win64_console.exe" | Select-Object -First 1 -ExpandProperty FullName
& $dn build "$PSScriptRoot\Instinct.Game.csproj" -v q --nologo | Select-String -Pattern " error " | Select-Object -First 20
$a = @("--path", $PSScriptRoot, "--", "--seed", $seed, "--bench", $seconds)
if ($skip -gt 0) { $a += @("--skip", $skip) }
if ($hide) { $a += @("--hide", $hide) }
& $godot @a | Select-String -Pattern "BENCH|ERROR|Exception|   at " | Select-Object -First 30

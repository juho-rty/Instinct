# 확인용: 판을 빨리 감아 한 장 찍는다.  예) .\shot.ps1 a 3 300 land
param([string]$name = "a", [int]$seed = 3, [int]$skip = 300, [string]$open = "", [float]$wait = 0, [float]$zoom = 0)
$dn = "C:\Program Files\dotnet\dotnet.exe"
$godot = Get-ChildItem "$env:LOCALAPPDATA\Microsoft\WinGet\Packages\GodotEngine.GodotEngine.Mono*" -Recurse -Filter "Godot_v*_mono_win64_console.exe" | Select-Object -First 1 -ExpandProperty FullName
$dir = Join-Path $env:TEMP "instinct-shots"
New-Item -ItemType Directory -Force $dir | Out-Null
$out = Join-Path $dir "$name.png"
& $dn build "$PSScriptRoot\Instinct.Game.csproj" -v q --nologo | Select-String -Pattern " error " | Select-Object -First 20
$a = @("--path", $PSScriptRoot, "--", "--seed", $seed, "--skip", $skip, "--shot", $out)
if ($open) { $a += @("--open", $open) }
if ($wait -gt 0) { $a += @("--wait", $wait) }
if ($zoom -gt 0) { $a += @("--zoom", $zoom) }
& $godot @a | Select-String -Pattern "ERROR|Exception|at " | Select-Object -First 20
$out

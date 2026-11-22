# UI 전체 찍기: 부족 시절·나라·후반의 화면과 창을 모두 찍어 shots/ui 에 번호 붙여 모은다.  예) .\shots.ps1 [씨앗=3]
param([int]$seed = 3)
$dn = "C:\Program Files\dotnet\dotnet.exe"
$godot = Get-ChildItem "$env:LOCALAPPDATA\Microsoft\WinGet\Packages\GodotEngine.GodotEngine.Mono*" -Recurse -Filter "Godot_v*_mono_win64_console.exe" | Select-Object -First 1 -ExpandProperty FullName
$dir = Join-Path (Split-Path $PSScriptRoot) "shots\ui"
New-Item -ItemType Directory -Force $dir | Out-Null
Get-ChildItem $dir -Filter *.png | Remove-Item
& $dn build "$PSScriptRoot\Instinct.Game.csproj" -v q --nologo | Select-String -Pattern " error " | Select-Object -First 20
# 이름, 건너뛸 해, 열 것, 기다릴 초, 확대
$list = @(
  @("01-부족-첫화면", 0, "", 0, 0),
  @("02-부족-확대", 0, "", 2, 2.2),
  @("03-부족-무리탭", 0, "camp:tribe", 0, 0),
  @("04-부족-일탭", 0, "camp:work", 0, 0),
  @("05-부족-건설탭-짓기모드", 0, "build", 0, 0),
  @("06-부족-자원증감", 0, "gain", 0, 0),
  @("07-부족-사람들", 0, "people", 0, 0),
  @("08-부족-법전", 0, "law", 0, 0),
  @("09-부족-기술", 0, "tech", 0, 0),
  @("10-부족-연대기", 0, "log", 0, 0),
  @("11-부족-나라창", 0, "realm", 0, 0),
  @("12-안내1", 0, "guide1", 0, 0),
  @("13-안내2", 0, "guide2", 0, 0),
  @("14-안내3", 0, "guide3", 0, 0),
  @("15-안내4", 0, "guide4", 0, 0),
  @("16-안내5", 0, "guide5", 0, 0),
  @("17-안내6", 0, "guide6", 0, 0),
  @("18-안내7", 0, "guide7", 0, 0),
  @("19-결정카드", 0, "decision", 0, 0),
  @("20-설정", 0, "settings", 0, 0),
  @("21-저장", 0, "save", 0, 0),
  @("22-불러오기", 0, "load", 0, 0),
  @("23-새판", 0, "new", 0, 0),
  @("31-나라-첫화면", 300, "", 0, 0),
  @("32-나라-흘러가는중", 300, "", 3, 0),
  @("33-나라-땅고름", 300, "land", 0, 0),
  @("34-나라-군대", 300, "army", 0, 0),
  @("35-나라-일탭", 300, "camp:work", 0, 0),
  @("36-나라-건설탭", 300, "camp:build", 0, 0),
  @("37-나라-나라창", 300, "realm", 0, 0),
  @("38-나라-법전", 300, "law", 0, 0),
  @("39-나라-기술", 300, "tech", 0, 0),
  @("40-나라-연대기", 300, "log", 0, 0),
  @("41-나라-사람들", 300, "people", 0, 0),
  @("42-나라-승리창", 300, "victory", 0, 0),
  @("51-후반-첫화면", 500, "", 0, 0),
  @("52-후반-기술", 500, "tech", 0, 0),
  @("53-후반-나라창", 500, "realm", 0, 0)
)
foreach ($s in $list) {
  $out = Join-Path $dir "$($s[0]).png"
  $a = @("--path", $PSScriptRoot, "--", "--seed", $seed, "--skip", $s[1], "--shot", $out)
  if ($s[2]) { $a += @("--open", $s[2]) }
  if ($s[3] -gt 0) { $a += @("--wait", $s[3]) }
  if ($s[4] -gt 0) { $a += @("--zoom", $s[4]) }
  & $godot @a | Select-String -Pattern "ERROR|Exception" | Select-Object -First 5
  if (Test-Path $out) { "찍음 $($s[0])" } else { "못 찍음 $($s[0])" }
}

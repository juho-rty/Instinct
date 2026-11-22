# Instinct — 본능 문명 시뮬레이터

세대가 흐르며 문명이 커지는 군락 시뮬레이션 게임(스팀 출시 목표)의 시뮬레이션 코어.
전체 구조는 **`STRUCTURE.md`** 에 있다. 작업 전에 먼저 읽는다.

## 반드시 지킬 것

1. **구조도를 항상 같이 고친다.** `src/`의 시뮬레이터를 고치면(제도, 본능, `Step()`의 단계, 이웃 상태, 파일, 실행 옵션, 죽음·이탈 원인, 관계 전이) 같은 작업 안에서 `STRUCTURE.md`의 해당 표·그림과 "마지막 갱신" 줄을 고친다. 구조도가 뒤처지면 `StructureDocTests`가 실패한다 — 테스트를 느슨하게 만들어 통과시키지 말고 문서를 고친다.
2. **작업을 끝내기 전에 테스트를 돌린다.** 새 제도나 드물게 터지는 경로를 넣으면 그 경로를 상태를 만들어 강제로 밟는 테스트를 `tests/Instinct.Tests`에 붙인다.
3. **결정성을 깨지 않는다.** 난수는 `Simulation._rng` 하나만 쓴다. 순서가 정해지지 않은 컬렉션을 돌 때는 `Id`로 정렬한다. 지도(`WorldMap.Generate`)는 `_rng`를 쓰지 않고 씨앗의 해시로만 만든다.
4. 연대기 문장은 한국어, 조사는 `Josa` 도우미로 붙인다.
5. 모든 제도는 얻는 것과 치르는 값이 함께 있어야 한다. 값 없는 제도는 넣지 않는다.

## 명령

이 PC에서는 `dotnet`이 PATH에 없을 수 있다. 그러면 전체 경로를 쓴다: `C:\Program Files\dotnet\dotnet.exe`.

```
dotnet build src/Instinct.Runner -c Release
dotnet test tests/Instinct.Tests
dotnet run --project src/Instinct.Runner -c Release -- --seed 1 --years 150 --private@30 --alliance@30
```

# Instinct — 본능 문명 시뮬레이터

세대가 흐르며 작은 무리에서 국가·제국으로 자라나는 본능 기반 문명 시뮬레이션 게임입니다.

- **시뮬레이션 코어 (`src/Instinct.Sim`)**: 순수 C# 결정론적 시뮬레이터 (단일 난수 스트림, 명령 기록 기반 세이브·리플레이)
- **게임 클라이언트 (`game/`)**: Godot 4 (.NET / C#) 클라이언트
- **자산 제작 도구 (`tools/`)**: Blender 3D 스프라이트 렌더러 및 SVG 아이콘 생성 스크립트
- **단위 테스트 (`tests/Instinct.Tests`)**: 결정성, 제도·외교·전쟁·세이브 복원 및 구조도 동기화 검증
- 전체 설계 및 시스템 구조는 [`STRUCTURE.md`](STRUCTURE.md), 그래픽 자산 규격은 [`ASSETS.md`](ASSETS.md)에 정리되어 있습니다.

## 빌드 및 테스트

```powershell
dotnet build game/Instinct.Game.csproj
dotnet test tests/Instinct.Tests
```

## 저작권 안내 (All Rights Reserved)

Copyright (c) juho-rty. **All Rights Reserved.**

이 저장소는 열람 및 참고 목적으로 공개되어 있습니다. 별도의 서면 허가 없이 본 저장소의 소스 코드, 그래픽 자산, 문서의 전부 또는 일부를 복제, 수정, 재배포하거나 상업적으로 이용할 수 없습니다.

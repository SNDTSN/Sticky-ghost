# App.UI

Avalonia 기반 뷰/뷰모델. Windows/Mac 공용 코드베이스 — **라이브러리**이며 직접 실행되지 않는다.
실행 진입점(WinExe) 및 플랫폼 구현 조립은 `App.Windows` 참고.

- to-do 리스트 창, 메모 위젯 창, 캐릭터 오버레이 창
- 캐릭터 팩 매니페스트를 실제 비트맵으로 합성해 그리는 렌더링 로직
- `App.Platform`의 인터페이스를 DI로 주입받아 사용 (창 클릭스루/투명도 설정 등, `ISecretStore` 포함) — 구체 구현(`App.Platform.Windows` 등)은 절대 직접 참조하지 않는다

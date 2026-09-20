# App.Core

플랫폼 무관 도메인 로직.

- to-do / 메모 도메인 모델 및 서비스 (`docs/todo-design.md` 참고)
- SQLite 리포지토리 구현 (플랫폼 무관 패키지라 별도 인프라 프로젝트로 안 뺌)
- LLM 어댑터 인터페이스 및 OpenAI/Gemini 등 구현체
- 캐릭터 팩(PNG 레이어 + JSON 매니페스트) 로딩/파싱
- 이벤트 버스 (`ITodoEventBus` 등)

`App.Platform`의 인터페이스에만 의존하고, 구체 구현(`App.Platform.Windows` 등)은 참조하지 않는다.
- 진단 로그 `AppLog` (`Diagnostics/`, `%LocalAppData%\StickyGhost\logs\app.log`) — 안전망이 삼킨 예외의 흔적 (`docs/stability-hardening.md`)

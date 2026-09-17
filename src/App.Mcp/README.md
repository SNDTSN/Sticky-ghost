# App.Mcp

Claude MCP 서버 모듈. `App.Core`에서 분리된 인프라 성격의 프로젝트 (stdio/소켓 리슨, MCP 프로토콜 처리).

- Claude Code/Desktop이 클라이언트로 접속했을 때 노출할 도구(`say`, `set_expression` 등) 정의
- `App.Core`의 이벤트 버스(`ITodoEventBus` 등)를 구독해 캐릭터 반응을 트리거하는 로직은 여기서 구현 (Core는 MCP 프로토콜을 몰라야 함)

상세 설계는 캐릭터 엔진 설계 단계에서 별도 문서로 다룰 예정.

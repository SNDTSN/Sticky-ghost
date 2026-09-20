# App.Mcp

Claude MCP 서버 모듈. `App.Core`에서 분리된 인프라 성격의 프로젝트 — Claude Code/Desktop이 stdio로 직접 스폰하는 **별도 프로세스**다.

- 노출하는 툴은 `say(text)`, `set_expression(expressionId)` 둘뿐이다(툴 표면 최소화 — 파일시스템/셸/네트워크 툴은 만들지 않는다).
- 하는 일은 명명 파이프 IPC(`App.Core/Infrastructure/Ipc`의 `CharacterIpcClient`)로 화면에 떠 있는 `App.UI` 프로세스에 요청을 전달하는 것뿐이다.
  실제로 말풍선을 띄우고 표정을 바꾸는 것은 `App.UI`(`CharacterIpcRequestHandler`)다.
- `ITodoEventBus`를 구독하지 않는다(초기 설계 메모에는 그렇게 적혀 있었으나 IPC 구조로 확정되며 바뀜). Core는 MCP 프로토콜을 몰라야 한다.
- 이중 실행 시 기존 창을 앞으로 가져오는 IPC 요청 `"activate"`는 이 프로젝트의 툴로 노출하지 않는다.

상세 설계: `docs/character-widget-design.md`의 "`App.Mcp` — 명명 파이프 IPC + `say`/`set_expression` 툴".

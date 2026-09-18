# Sticky Ghost 설계 초안

## 구현할 기능
Sticky Ghost(이하 본 프로젝트)는 우카가카(伺か)에서 영감을 얻은 프로젝트로 간단한 상호작용이 가능한 캐릭터를 **갈아끼울 수 있는** Windows 전용 스탠드얼론 프로그램이다. 한국인 사용자를 대상으로 하며, 이하 3개의 중요 부분으로 나뉜다.
### To-do list
* 이 프로그램의 코어. 할 일의 카테고리, 중요도, 일자 등을 지정하여 깔끔한 목록으로 표시할 수 있어야 한다.
* 상세 설계는 [docs/todo-design.md](./todo-design.md) 참고 (데이터 모델, 반복 일정 처리, SQLite 스키마 확정됨).

### 메모 위젯
* to-do에 따라오는 부수적인 것. windows의 포스트잇 모양 메모 위젯이 사라져서 아쉬운 마음에 좀 비슷한 걸 넣어 보고 싶었다.

### 간단한 상호작용이 가능한 캐릭터
* 사용자가 필수로 사용해야 하는 기능은 아니지만, 굳이 이 프로그램을 사용하는 이유는 바로 이 기능 때문일 것이다. 마스코트 적인 위치.
* LLM을 연결하여 간단한 대화를 주고받을 수 있으며, 본가 우카가카와 달리 미리 정해진 대사 데이터베이스가 아니라 LLM이 생성한 랜덤 대사를 출력한다.
* 가능하다면 어댑터를 여러 개 준비해 사용자들이 각자 자신이 구독 중인 LLM 서비스를 그대로 이용할 수 있도록 한다.
* 단, Claude 같은 경우 서드파티 도구의 구독 OAuth 토큰 사용을 약관으로 금지하였으므로 MCP 도구를 통해 Claude code와 연결하는 등의 우회 방식이 필요하다.
* 캐릭터의 외형과 성격을 변경할 수 있어야 한다.

## 중요한 점
* 본가 우카가카처럼 메모리 누수가 심하면 안 된다. 첫째도 메모리 관리, 둘째도 메모리 관리.
* Windows 전용으로 개발되지만 Mac 사용자가 언제든 고쳐 쓸 수 있도록 프레임워크를 Windows/Mac 겸용으로 구축한다.

## 아키텍처 결정

* **코어 스택**: C#/.NET, UI는 처음부터 **Avalonia 단일 코드베이스**로 작성 (Windows/Mac 뷰·뷰모델 재사용). OS 종속 기능(투명/클릭스루 창, 자격 증명 저장, 유휴 감지 등)만 `App.Platform` 인터페이스 뒤로 격리해 플랫폼별로 구현체를 교체하는 구조. 상세 인터페이스 목록은 [docs/PORTING.md](./PORTING.md) 참고.
* **LLM 연동 모델**: OpenAI/Gemini 등은 위젯이 능동적으로 호출하는 어댑터 방식. Claude는 서드파티의 구독 OAuth 토큰 재사용이 약관 위반이므로, 위젯이 MCP 서버가 되어 Claude Code/Desktop이 클라이언트로 붙는 수동 반응 모델 사용 (`stackchan-mcp`와 동일 패턴). 이 때문에 Claude 연동은 다른 어댑터와 달리 캐릭터가 "스스로 말 거는" 동작이 제한됨.
* **캐릭터 에셋 포맷**: PNG 레이어 + JSON 매니페스트. 모딩 자유도를 우선하여 Live2D/Spine 같은 유료·라이선스 종속 툴체인은 배제.
* **모듈 구조** (`src/` 하위):
  * `App.Core` — to-do/메모 도메인 로직, SQLite 리포지토리 구현(플랫폼 무관이므로 Core에 포함), LLM 어댑터, 캐릭터 팩 로딩, 이벤트 버스
  * `App.UI` — Avalonia 뷰/뷰모델
  * `App.Platform` — 플랫폼 인터페이스 정의만 (`IWindowBehavior`, `ISecretStore`, `IIdleDetector`)
  * `App.Platform.Windows` — Windows 구현체 (P/Invoke, DPAPI 등)
  * `App.Platform.Stub` — 아무 동작도 하지 않는 기본/테스트용 구현체
  * `App.Mcp` — Claude MCP 서버 모듈 (Core에서 분리, 인프라 성격)

## 참고 리포지토리
* [雨衣ちゃんMCP](https://github.com/Uncle-Peke/ui-chan-mcp)
  * MCP 서버 = 캐릭터 위젯 구조. Electron + TS, PSD 레이어 표정, VoiSona Talk TTS 연동. Claude 연동(Model A: 수동 반응) 아키텍처 참고용.

### GitHub 토픽 서베이
* [topics/ukagaka](https://github.com/topics/ukagaka?o=desc&s=updated) — 우카가카 계열 베이스웨어/고스트
  * ninix-kagari (Tatakinov) — ninix-aya 포크, 데스크톱 펫 베이스웨어
  * Utatane (opera7133) — macOS용 우카가카 호환 베이스웨어 (Swift)
  * yaya-shiori (YAYA-shiori) — Shiori 개발 엔진 (C++)
  * mp-ukagaka (Horlicks-p) — OpenAI/Claude/Ollama/Gemini 연동 사례
* [topics/desktop-mascot](https://github.com/topics/desktop-mascot?o=asc&s=forks) — 데스크톱 마스코트 전반
  * live2d-companion (thanhphuong080199) — Live2D + Ollama 로컬 AI 채팅, API 키 불필요, Electron
  * Mandarin (Mandarin715) — Galgame풍 立ち絵 연출 + LLM 대화 + VITS TTS (C++)
  * PugTop — Tauri 기반 항상 위 표시 데스크톱 펫 (스택 후보 Tauri 참고용)
  * MiMi — PySide6 기반 Windows 펫, 마우스 추적/눈 애니메이션

# App.Platform

플랫폼 인터페이스 정의만 포함 (구현 없음). 상세는 `docs/PORTING.md` 참고.

- `IWindowBehavior`
- `ISecretStore`
- `IIdleDetector`

새 인터페이스가 필요해지면 여기에 추가하고, `docs/PORTING.md`에 기대 동작·Windows API·Mac 후보 API를 같이 기록한다.

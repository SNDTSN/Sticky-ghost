# 플랫폼 이식 가이드 (Windows → Mac)

`App.Platform`에 정의된 인터페이스와, 각 메서드가 어떤 동작을 보장해야 하는지, Windows 구현이 쓰는 API, Mac 이식 시 후보 API를 정리한 문서. Mac 이식을 담당하게 될 사람(또는 미래의 나)이 이 문서만 보고 `App.Platform.Mac`을 새로 만들 수 있는 걸 목표로 한다.

## 설계 원칙

* `App.Platform`은 **인터페이스만** 정의한다. 구현은 `App.Platform.Windows`, `App.Platform.Stub`, (추후) `App.Platform.Mac`에 둔다.
* `App.Core`, `App.UI`는 이 인터페이스만 알고, 구체 구현을 직접 참조하지 않는다 (DI로 주입).
* `App.Platform.Stub`은 항상 최신 상태로 유지한다. 새 인터페이스/메서드를 추가하면 Stub 구현도 같이 추가해서, 특정 플랫폼 구현이 없어도 Core/UI 개발과 테스트가 막히지 않게 한다.

## 구현 상태

* `App.Platform` — 아래 3개 인터페이스 정의 완료 (`src/App.Platform/*.cs`).
* `App.Platform.Stub` — 3개 다 구현 완료 (`src/App.Platform.Stub/*.cs`). `StubIdleDetector`는 문서에 적힌 대로 테스트용 세터(`SetSimulatedIdleDuration`)를 제공.
* `App.Platform.Windows` — 아직 미착수 (csproj만 존재, 실제 P/Invoke 구현 없음). 이 문서의 "Windows 구현" 열이 착수 시 그대로 작업 기준이 됨.

## IWindowBehavior

캐릭터 오버레이 창, 메모 위젯 창처럼 일반적인 앱 창과 다르게 동작해야 하는 네이티브 창 제어. Avalonia의 `Window` 자체 속성(`Topmost` 등)으로 커버되지 않는, 플랫폼별 네이티브 API가 필요한 부분만 다룬다.

| 메서드 | 기대 동작 | Windows 구현 | Mac 후보 API |
|---|---|---|---|
| `SetClickThrough(handle, enabled)` | true면 창이 마우스 이벤트를 받지 않고 아래 창/바탕화면으로 흘려보냄 (캐릭터가 화면을 가리지만 클릭은 안 막아야 할 때) | `SetWindowLong(GWL_EXSTYLE, WS_EX_TRANSPARENT \| WS_EX_LAYERED)` (user32.dll) | `NSWindow.ignoresMouseEvents = true` |
| `SetPerPixelTransparency(handle, enabled)` | 사각형이 아닌 캐릭터 스프라이트(PNG alpha) 모양 그대로 창 외곽선을 렌더링 | `WS_EX_LAYERED` + `UpdateLayeredWindow` | `NSWindow.isOpaque = false` + `backgroundColor = .clear`, 레이어 기반 컨텐츠 뷰 |
| `SetAlwaysOnTop(handle, enabled)` | 다른 일반 창들보다 항상 위에 표시 | `SetWindowPos(HWND_TOPMOST, ...)` | `NSWindow.level = .floating` |
| `ExcludeFromTaskbar(handle, enabled)` | 작업표시줄/Dock에 아이콘이 뜨지 않게 함 | `WS_EX_TOOLWINDOW` 스타일 추가 | `NSWindow.styleMask`에서 창을 `NSPanel` + `.nonactivatingPanel`로 구성하거나 `NSApp.setActivationPolicy(.accessory)` |

**Stub 동작**: 아무것도 하지 않음 (일반 창처럼 동작). 개발 중 로직 확인용으로 충분.

## ISecretStore

LLM 어댑터(OpenAI/Gemini 등)의 API 키를 암호화해서 로컬에 저장. 절대 평문으로 저장하지 않는다.

| 메서드 | 기대 동작 | Windows 구현 | Mac 후보 API |
|---|---|---|---|
| `SaveSecret(key, value)` | 값을 암호화해서 로컬에 영구 저장 (덮어쓰기) | DPAPI — `ProtectedData.Protect` (`System.Security.Cryptography.ProtectedData`), 결과를 앱 로컬 데이터 폴더에 파일로 저장 | Keychain Services (`Security.framework`) — `SecItemAdd`/`SecItemUpdate`를 P/Invoke로 호출하거나 네이티브 헬퍼 바인딩 사용 |
| `TryGetSecret(key)` | 저장된 값을 복호화해서 반환, 없으면 null | `ProtectedData.Unprotect` | `SecItemCopyMatching` |
| `DeleteSecret(key)` | 저장된 값 삭제 | 파일 삭제 | `SecItemDelete` |

**주의**: DPAPI는 사용자 계정+머신에 바인딩되므로 다른 PC로 마이그레이션 시 재입력이 필요함 — 이건 의도된 동작(비밀 유출 방지)이라 UX 문서에도 명시할 것.

**Stub 동작**: 메모리 딕셔너리에만 저장 (프로세스 종료 시 소실). 절대 실제 시크릿 저장 용도로 쓰지 않음, 테스트 전용.

## IIdleDetector

캐릭터의 Idle 상태(가만히 있을 때의 애니메이션/대사) 트리거용으로 사용자의 유휴 시간을 조회.

| 메서드 | 기대 동작 | Windows 구현 | Mac 후보 API |
|---|---|---|---|
| `GetIdleDuration()` | 마지막 키보드/마우스 입력 이후 경과 시간을 반환 | `GetLastInputInfo` (user32.dll) + `GetTickCount64`로 경과 계산 | `CGEventSourceSecondsSinceLastEventType(kCGEventSourceStateHIDSystemState, kCGAnyInputEventType)` (Core Graphics) |

**Stub 동작**: 항상 `TimeSpan.Zero` 반환 (또는 테스트에서 값을 임의로 설정할 수 있는 세터 제공).

## 다음에 채울 것

캐릭터 엔진/메모 위젯 설계가 진행되면서 트레이 아이콘, 전역 단축키, 알림(토스트) 등 추가 플랫폼 인터페이스가 필요해질 수 있음. 필요해지는 시점에 이 문서에 표를 추가한다 — 미리 만들어두지 않는다.

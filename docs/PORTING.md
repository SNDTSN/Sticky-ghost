# 플랫폼 이식 가이드 (Windows → Mac)

`App.Platform`에 정의된 인터페이스와, 각 메서드가 어떤 동작을 보장해야 하는지, Windows 구현이 쓰는 API, Mac 이식 시 후보 API를 정리한 문서. Mac 이식을 담당하게 될 사람(또는 미래의 나)이 이 문서만 보고 `App.Platform.Mac`을 새로 만들 수 있는 걸 목표로 한다.

## 설계 원칙

* `App.Platform`은 **인터페이스만** 정의한다. 구현은 `App.Platform.Windows`, `App.Platform.Stub`, (추후) `App.Platform.Mac`에 둔다.
* `App.Core`, `App.UI`는 이 인터페이스만 알고, 구체 구현을 직접 참조하지 않는다 (DI로 주입). 실제로 구체 구현을 `new`해서
  주입하는 조립 루트는 플랫폼별 실행 진입점 프로젝트(Windows는 `App.Windows`, TargetFramework `net8.0-windows`)뿐이다.
  `App.UI`가 실수로 `App.Platform.Windows`를 참조하려 하면 TargetFramework 불일치(`net8.0` vs `net8.0-windows`)로
  빌드가 바로 깨지므로, 이 원칙이 프로젝트 파일 레벨에서 강제된다. Mac 이식 시에도 같은 패턴으로 `App.Mac` 진입점
  프로젝트를 추가하면 된다 (`src/App.Windows/README.md` 참고).
* `App.Platform.Stub`은 항상 최신 상태로 유지한다. 새 인터페이스/메서드를 추가하면 Stub 구현도 같이 추가해서, 특정 플랫폼 구현이 없어도 Core/UI 개발과 테스트가 막히지 않게 한다.

## 구현 상태

* `App.Platform` — 아래 3개 인터페이스 정의 완료 (`src/App.Platform/*.cs`).
* `App.Platform.Stub` — 3개 다 구현 완료 (`src/App.Platform.Stub/*.cs`). `StubIdleDetector`는 문서에 적힌 대로 테스트용 세터(`SetSimulatedIdleDuration`)를 제공.
* `App.Platform.Windows` — `ISecretStore`(`DpapiSecretStore`)와 `IWindowBehavior`(`WindowsWindowBehavior`, 2026-09-20)를 구현했고
  `App.Windows`(실행 진입점)가 `App.UI.App.SecretStoreFactory`/`WindowBehaviorFactory` 델리게이트로 `MainWindow`에 주입한다.
  `WindowsWindowBehavior`는 지금 필요한 `SetInputShape`(캐릭터 오버레이의 투명 영역 클릭 통과)와 `SetAlwaysOnTop`(메모 📌)만
  구현했고, 2026-09-22에 `RestoreImeBinding`(한글 IME 바인딩 복구, #24)을 추가했다. `SetClickThrough`/`ExcludeFromTaskbar`는 no-op(필요해지면 구현). `IIdleDetector`의 Windows 구현은 아직 미착수.
  `App.UI`는 더 이상 `App.Platform.Stub`을 참조하지 않는다.
  캐릭터 오버레이 창/말풍선은 `Topmost`, `ShowInTaskbar`, `TransparencyLevelHint=Transparent`, `SystemDecorations=None` 같은
  Avalonia 기본 속성으로 구성하고, Avalonia로 안 되는 "투명 픽셀 클릭 통과"만 `SetInputShape`로 처리한다.

## IWindowBehavior

캐릭터 오버레이 창, 메모 위젯 창처럼 일반적인 앱 창과 다르게 동작해야 하는 네이티브 창 제어. Avalonia의 `Window` 자체 속성(`Topmost` 등)으로 커버되지 않는, 플랫폼별 네이티브 API가 필요한 부분만 다룬다.

| 메서드 | 기대 동작 | Windows 구현 | Mac 후보 API |
|---|---|---|---|
| `SetClickThrough(handle, enabled)` | true면 창이 마우스 이벤트를 받지 않고 아래 창/바탕화면으로 흘려보냄 (캐릭터가 화면을 가리지만 클릭은 안 막아야 할 때) | `SetWindowLong(GWL_EXSTYLE, WS_EX_TRANSPARENT \| WS_EX_LAYERED)` (user32.dll) | `NSWindow.ignoresMouseEvents = true` |
| `SetInputShape(handle, rects)` | `rects`(창 좌상단 기준 물리 픽셀, 서로 겹치지 않는 `MaskRect` 목록) 안쪽만 마우스 입력을 받고 렌더링되며, 밖은 아래 창으로 통과. 투명 PNG 캐릭터의 투명한 부분이 뒤에 있는 창 클릭을 막지 않게 하는 용도. `null`이면 제한 해제 | `ExtCreateRegion`(RGNDATA) + `SetWindowRgn` (gdi32/user32). 성공 시 리전 소유권이 OS로 넘어가므로 `DeleteObject` 금지, 실패 시에만 해제. 빈 목록은 창이 보이지도 눌리지도 않게 되므로 무시 | 알파 0 픽셀은 기본적으로 클릭이 통과되므로 no-op으로 시작해도 될 가능성이 큼(이식 시 실측). 필요하면 `NSWindow` 컨텐츠 뷰의 `hitTest` 재정의 |
| `SetAlwaysOnTop(handle, enabled)` | 다른 일반 창들보다 항상 위에 표시 | `SetWindowPos(HWND_TOPMOST, ...)` | `NSWindow.level = .floating` |
| `ExcludeFromTaskbar(handle, enabled)` | 작업표시줄/Dock에 아이콘이 뜨지 않게 함 | `WS_EX_TOOLWINDOW` 스타일 추가 | `NSWindow.styleMask`에서 창을 `NSPanel` + `.nonactivatingPanel`로 구성하거나 `NSApp.setActivationPolicy(.accessory)` |
| `RestoreImeBinding()` | IME(한글 조합) 입력을 받을 창을 지금 포커스를 가진 창으로 되돌림. 활성화 없이 뜨는 창(`ShowActivated="False"`인 캐릭터·말풍선)을 만들거나 닫은 직후에 호출. 포커스·z-order는 건드리지 않음 | `GetFocus()`가 준 창에 `WM_INPUTLANGCHANGE`(lParam = `GetKeyboardLayout(0)`)를 `SendMessage`. Avalonia가 이 메시지에서 전역 IME 싱글턴을 그 창으로 다시 묶는다. `GetFocus()`가 0(다른 앱이 포커스)이면 아무것도 안 함 — 돌아올 때 `WM_ACTIVATE`가 알아서 고친다. 근거는 KNOWN_ISSUES #24 | Avalonia macOS 백엔드는 IME 상태를 `IAvnWindow`별로 들고 있어 같은 문제가 없을 가능성이 큼 → no-op으로 시작하고, 이식 시 오버레이를 띄운 채 한글 입력을 실측해 확인 |

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

## 실행 진입점 소관 — 단일 인스턴스 (2026-09-20)

`App.Platform` 인터페이스가 아니라 **실행 진입점 프로젝트**(Windows는 `App.Windows/SingleInstanceGuard.cs`)가 직접 구현하는 부분.
`App.UI`/`App.Core`는 이 구현을 모르고, 기존 인스턴스를 깨우는 통로로 `App.Core`의 IPC 요청 타입 `"activate"`만 공유한다
(`CharacterIpcRequest.TypeActivate`, 수신 측은 `App.UI`의 `CharacterIpcRequestHandler` → `MainWindow.ActivateSelf`). Mac 이식 시
`App.Mac`이 아래 표의 대응을 구현하면 된다.

| 기능 | 기대 동작 | Windows 구현 | Mac 후보 API |
|---|---|---|---|
| 단일 인스턴스 | 앱이 이미 떠 있으면 두 번째 실행은 창을 띄우지 않고 종료 | 명명 뮤텍스 `Local\StickyGhost.SingleInstance` | `NSRunningApplication.runningApplications(withBundleIdentifier:)`로 중복 검사(또는 잠금 파일) |
| 기존 창 앞으로 | 두 번째 실행이 기존 인스턴스의 메인 창을 복원하고 앞으로 가져오게 함 | IPC `"activate"` 전송 + 전송 전 `AllowSetForegroundWindow(ASFW_ANY)`(포그라운드 권한 위임) | `NSRunningApplication.activate(options:)` (IPC 없이도 가능) |

상세 트레이드오프와 롤백: `docs/stability-hardening.md` "C1".

## 다음에 채울 것

캐릭터 엔진/메모 위젯 설계가 진행되면서 트레이 아이콘, 전역 단축키, 알림(토스트) 등 추가 플랫폼 인터페이스가 필요해질 수 있음. 필요해지는 시점에 이 문서에 표를 추가한다 — 미리 만들어두지 않는다.

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
  `WindowsWindowBehavior`는 지금 필요한 `SetInputShape`(캐릭터 오버레이의 투명 영역 클릭 통과)와
  `RestoreImeBinding`(한글 IME 바인딩 복구, #24, 2026-09-22 추가)을 구현했다. `SetClickThrough`는 no-op(필요해지면 구현).
  `IIdleDetector`의 Windows 구현은 아직 미착수.
  `App.UI`는 더 이상 `App.Platform.Stub`을 참조하지 않는다.
  캐릭터 오버레이 창/말풍선은 `Topmost`, `ShowInTaskbar`, `TransparencyLevelHint=Transparent`, `SystemDecorations=None` 같은
  Avalonia 기본 속성으로 구성하고, Avalonia로 안 되는 "투명 픽셀 클릭 통과"만 `SetInputShape`로 처리한다.

## IWindowBehavior

캐릭터 오버레이 창, 메모 위젯 창처럼 일반적인 앱 창과 다르게 동작해야 하는 네이티브 창 제어. Avalonia의 `Window` 자체 속성(`Topmost` 등)으로 커버되지 않는, 플랫폼별 네이티브 API가 필요한 부분만 다룬다.

| 메서드 | 기대 동작 | Windows 구현 | Mac 후보 API |
|---|---|---|---|
| `SetClickThrough(handle, enabled)` | true면 창이 마우스 이벤트를 받지 않고 아래 창/바탕화면으로 흘려보냄 (캐릭터가 화면을 가리지만 클릭은 안 막아야 할 때) | `SetWindowLong(GWL_EXSTYLE, WS_EX_TRANSPARENT \| WS_EX_LAYERED)` (user32.dll) | `NSWindow.ignoresMouseEvents = true` |
| `SetInputShape(handle, rects)` | `rects`(창 좌상단 기준 물리 픽셀, 서로 겹치지 않는 `MaskRect` 목록) 안쪽만 마우스 입력을 받고 렌더링되며, 밖은 아래 창으로 통과. 투명 PNG 캐릭터의 투명한 부분이 뒤에 있는 창 클릭을 막지 않게 하는 용도. `null`이면 제한 해제 | `ExtCreateRegion`(RGNDATA) + `SetWindowRgn` (gdi32/user32). 성공 시 리전 소유권이 OS로 넘어가므로 `DeleteObject` 금지, 실패 시에만 해제. 빈 목록은 창이 보이지도 눌리지도 않게 되므로 무시 | 알파 0 픽셀은 기본적으로 클릭이 통과되므로 no-op으로 시작해도 될 가능성이 큼(이식 시 실측). 필요하면 `NSWindow` 컨텐츠 뷰의 `hitTest` 재정의 |
| `RestoreImeBinding()` | IME(한글 조합) 입력을 받을 창을 지금 포커스를 가진 창으로 되돌림. 활성화 없이 뜨는 창(`ShowActivated="False"`인 캐릭터·말풍선)을 만들거나 닫은 직후에 호출. 포커스·z-order는 건드리지 않음 | `GetFocus()`가 준 창에 `WM_INPUTLANGCHANGE`(lParam = `GetKeyboardLayout(0)`)를 `SendMessage`. Avalonia가 이 메시지에서 전역 IME 싱글턴을 그 창으로 다시 묶는다. `GetFocus()`가 0(다른 앱이 포커스)이면 아무것도 안 함 — 돌아올 때 `WM_ACTIVATE`가 알아서 고친다. 근거는 KNOWN_ISSUES #24 | Avalonia macOS 백엔드는 IME 상태를 `IAvnWindow`별로 들고 있어 같은 문제가 없을 가능성이 큼 → no-op으로 시작하고, 이식 시 오버레이를 띄운 채 한글 입력을 실측해 확인 |

**Stub 동작**: 아무것도 하지 않음 (일반 창처럼 동작). 개발 중 로직 확인용으로 충분.

### 여기에 넣지 않는 것 — Avalonia 속성으로 되는 것 (2026-09-22 정리)

원칙으로만 적어둔 게 아니라 실제로 버그가 나서 되돌린 항목들이다. Mac 구현에서도 같은 실수를 하지 말 것.

* **`Topmost`** — 메모 위젯의 📌(항상 위 표시). 2026-09-20~22 사이에는 `SetAlwaysOnTop(handle, enabled)` →
  `SetWindowPos(HWND_TOPMOST, …)`로 직접 올렸는데, 이러면 **Avalonia는 그 창이 topmost인 줄 모른다**(`Window.Topmost`가 false로 남는다).
  그 결과 메모의 삭제 확인 대화상자가 소유자의 topmost를 물려받을 수 없어 메모 **뒤로** 숨었고, 모달이라 메모까지 잠겨
  사용자가 빠져나갈 수 없었다(KNOWN_ISSUES #25). 지금은 `MemoWindow.axaml`의 `Topmost="{Binding IsPinned}"`가 처리하고,
  인터페이스에서 `SetAlwaysOnTop`을 제거했다. **Mac 구현에서도 `NSWindow.level = .floating`을 직접 만지지 말 것** —
  Avalonia macOS 백엔드가 같은 일을 하고, 직접 만지면 똑같이 Avalonia가 모르는 상태가 된다.
* **`ShowInTaskbar`** — `ExcludeFromTaskbar(handle, enabled)`가 선언돼 있었지만 호출부가 한 곳도 없었고 Windows 구현도 no-op이었다.
  실제로 일하는 것은 `CharacterWindow.axaml`/`SpeechBubbleWindow.axaml`의 `ShowInTaskbar="False"`다. 같이 제거했다.
* **`TransparencyLevelHint`, `SystemDecorations`** — 처음부터 Avalonia 속성으로만 쓰고 있다.

판단 기준: **"Avalonia가 이미 그 상태를 알고 있어야 하는가"**. 창의 상태(위/아래, 작업표시줄, 장식, 투명도)는 Avalonia 속성으로 두고,
Avalonia가 개념 자체를 모르는 것(투명 픽셀 클릭 통과, OS IME 바인딩)만 이 인터페이스로 내린다.

### 핸들(`IntPtr`)에 대한 주의 — Mac 이식 착수 시 **먼저** 볼 것 (2026-09-22)

`SetClickThrough`/`SetInputShape`는 네이티브 창 핸들을 `IntPtr` 하나로 받는다. 호출부(`App.UI`)는 Avalonia의
`TryGetPlatformHandle()?.Handle`로 이 값을 꺼낸다(`CharacterWindow.axaml.cs`의 `ApplyInputShape`).

문제는 `IPlatformHandle`에 핸들의 **종류**를 나타내는 `HandleDescriptor`가 같이 들어 있다는 점이다 — Windows는 `"HWND"`,
macOS는 `"NSWindow"`(또는 `"NSView"`). 지금은 `.Handle`만 꺼내면서 그 정보를 버리고 있고, 구현체는 받은 숫자가 무엇인지
검증하지 않는다. Windows 단독일 때는 드러나지 않지만, **Mac 구현을 쓰기 시작하는 순간 조용히 틀릴 수 있는 부분이다**
(받은 게 `NSWindow`인지 `NSView`인지에 따라 호출할 API가 다르다).

고칠 때 방향은 둘 중 하나:

1. **(권장)** `App.Platform`에 `readonly record struct NativeWindowHandle(IntPtr Value, string Kind)`를 두고 각 구현이
   `Kind`를 검증한 뒤 쓴다. `App.UI`가 `TryGetPlatformHandle()`의 `HandleDescriptor`를 그대로 실어 보내면 된다.
2. `IntPtr`을 유지하고, Mac 구현이 받는 핸들의 종류를 이 문서와 실측으로만 보장한다.

`IPlatformHandle`을 그대로 넘기는 선택지는 없다 — `App.Platform`이 Avalonia에 의존하게 되는데, 지금 `App.Platform`은
프로젝트/패키지 참조가 **0개**이고 위 "설계 원칙"이 그 성질에 기대고 있다.

**지금 고치지 않은 이유(2026-09-22 판단)**: Mac 실기기가 없어 어느 쪽이 맞는지 확인할 방법이 없다. Windows에서만 검증한
추상화를 미리 늘리기보다, 이식에 착수해 `TryGetPlatformHandle()`이 실제로 무엇을 주는지 본 뒤에 정하는 편이 낫다.

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

## 앱 데이터 폴더 경로 (2026-09-22)

DB·설정·창 위치·API 키·로그가 들어가는 로컬 데이터 폴더. **`App.Core/Infrastructure/FileSystem/AppPaths.cs` 한 곳에서만 만든다** —
예전에는 `AppLog`, `MainWindow`, `App.Windows/Program.cs`가 각자 `%LocalAppData%\StickyGhost`를 조립하고 있었다(2026-09-22 정리).

| 경로 | 쓰는 곳 | 현재 값 |
|---|---|---|
| `AppPaths.DataDir` | `stickyghost.db`, `settings.json`, `window-state.json`, `secrets/` | `%LocalAppData%\StickyGhost` |
| `AppPaths.LogFile` | `AppLog` | `%LocalAppData%\StickyGhost\logs\app.log` |

**Mac 이식 시 볼 것**: .NET에서 `Environment.SpecialFolder.LocalApplicationData`는 macOS에서 `~/.local/share`로 매핑된다.
동작은 하지만 맥 관례는 `~/Library/Application Support/<앱 이름>`이라 그쪽으로 바꾸려면 `AppPaths.DataDir` 한 줄만 고치면 된다.
플랫폼 분기가 필요해지면 `App.Platform` 인터페이스로 뺄 게 아니라 여기서 `OperatingSystem.IsMacOS()`로 갈라도 된다 —
OS 종속 API를 호출하는 게 아니라 경로 문자열만 고르는 일이기 때문이다(`AppSettingsStore`를 `App.Core`에 둔 것과 같은 판단).

**여기 넣지 않는 것**: 캐릭터 팩 폴더(`AppContext.BaseDirectory/CharacterPacks`)는 데이터가 아니라 앱과 함께 배포되는 자산이라
`MainWindow._packsRootDir`가 따로 만든다. 사용자가 팩을 추가하는 폴더를 데이터 폴더 쪽으로 옮기게 되면 그때 이 표에 합친다.

## 다음에 채울 것

캐릭터 엔진/메모 위젯 설계가 진행되면서 트레이 아이콘, 전역 단축키, 알림(토스트) 등 추가 플랫폼 인터페이스가 필요해질 수 있음. 필요해지는 시점에 이 문서에 표를 추가한다 — 미리 만들어두지 않는다.

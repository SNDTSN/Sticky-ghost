# 안정성 보강 (2026-09-20)

중간 점검(코드 전체 vs 설계 문서, 메모리 누수 위험 조사)에서 "메모리 누수는 아니지만 크래시/무한 루프로 이어지는 경로"
세 가지(C1~C3)가 발견되어 고친 변경 이력. **나중에 문제가 생겼을 때 어떤 변경이 원인인지 추적하고, 항목별로 되돌릴 수 있게**
항목마다 "왜 / 무엇을 / 트레이드오프 / 롤백 방법"을 적어 둔다.

> 점검에서 나온 나머지 항목(팩 이미지 상주·크기 상한 M1, `ApplyInputShape` LOH 쓰레기 M2, 문서 동기화 등)은 이 변경의 범위가 아니다.
> 이 문서는 A 묶음(크래시 방어)만 다룬다.

## 검증 상태

| 항목 | 상태 |
|---|---|
| 컴파일 (`dotnet build src/App.Windows`) | 통과 — 오류 0, 기존 AVLN3001 경고만(의도적으로 방치, 메모리 참고) |
| 실제 실행 검증 | **아직 안 함.** 아래 "확인 방법"으로 사용자가 확인해야 함 |
| 커밋 | 안 함. 롤백을 쉽게 하려면 이 변경만 한 커밋으로 묶어 두면 `git revert`로 통째로 되돌릴 수 있다 |

## 진단 로그 — `AppLog` (신규)

* **무엇**: `App.Core/Diagnostics/AppLog.cs`. `%LocalAppData%\StickyGhost\logs\app.log`에 `[시각] [카테고리] 내용`을 추가한다.
  1MB를 넘으면 `app.log.old` 하나로 교체(1세대만 보관). 로깅 실패는 삼킨다(안전망이 장애 원인이 되면 안 되므로).
* **왜**: 이번 변경들은 예외를 "삼키는" 안전망이 많다. 삼켰다는 흔적이 없으면 나중에 원인을 못 찾는다. 기존엔 로그가 전혀 없었다.
* **카테고리**: `ipc`, `pack`, `llm`, `ui`, `ui-unhandled`, `unhandled`, `unobserved-task`, `settings`, `single-instance`.
  문제가 생기면 먼저 이 파일에서 해당 카테고리를 본다.
* **주의**: 예외 전문(`ToString()`)을 남기므로 파일 경로 등이 들어갈 수 있다. **API 키가 들어갈 수 있는 예외는 로깅하지 않는다** —
  Gemini `Headers.Add`의 `FormatException` 메시지에는 헤더 값(=키)이 그대로 들어가서, 어댑터가 이 예외를 로그 없이 `Unauthorized`로
  바꾼다(C3 참고). 이 예외를 로깅하도록 바꾸면 키가 로그에 남는다.
* **롤백**: 각 호출부의 `AppLog.Write(...)` 줄을 지워도 나머지 동작은 그대로다. 파일 자체를 지우려면 사용처를 전부 정리해야 한다.

## C1. 이중 실행 시 UI 스레드 무한 루프

**증상(코드 읽기 기준, 재현은 안 해봄)**: 앱을 두 번 실행하면 두 번째 인스턴스의 `NamedPipeServerStream`(최대 인스턴스 1) 생성이
계속 예외를 던지고, 서버 accept 루프의 `catch { }`가 await 없이 곧바로 재시도해서 `MainWindow` 생성자 안의 `Start()`가 UI 스레드를
붙잡은 채 CPU 100%로 돈다(창이 안 뜸).

| 변경 | 파일 | 내용 |
|---|---|---|
| C1-a 단일 인스턴스 | `App.Windows/SingleInstanceGuard.cs`(신규), `App.Windows/Program.cs` | 명명 뮤텍스 `Local\StickyGhost.SingleInstance`. 이미 있으면 두 번째 프로세스는 기존 인스턴스에 `activate` 요청을 보내고 즉시 종료 |
| C1-b 기존 창 앞으로 | `CharacterIpcContract.cs`(`TypeActivate`), `CharacterIpcRequestHandler.cs`, `MainWindow.axaml.cs`(`ActivateSelf`) | IPC 요청 타입 `"activate"` 추가. 핸들러가 캐릭터 팩/표시 여부 검사보다 **먼저** 처리하고 UI 스레드에서 `WindowState` 복원 + `Activate()` |
| C1-c 포그라운드 권한 | `SingleInstanceGuard.NotifyExistingInstance` | Windows는 포그라운드 권한이 없는 프로세스의 창 전면화를 막는다. 방금 사용자가 실행한 두 번째 프로세스가 `AllowSetForegroundWindow(ASFW_ANY)`로 권한을 넘긴다 |
| C1-d 서버 루프 방어 | `CharacterIpcServer.cs` | 루프 시작에 `await Task.Yield()`(Start가 UI 스레드를 잡지 않게), `catch`에서 로그 + 지수 백오프(500ms→10s) |

* **동작**: 두 번째 실행 → 기존 메인 창이 최소화돼 있으면 복원되고 앞으로 나옴. 두 번째 프로세스는 창을 띄우지 않고 종료.
  메모/캐릭터 창은 앞으로 가져오지 않는다(메인 창만).
* **트레이드오프**
  * 기존 인스턴스가 아직 기동 중이라 파이프가 없으면 `ConnectAsync`가 3초 기다린 뒤 포기하고, 두 번째 프로세스는 아무 일 없이 종료한다
    (`single-instance` 로그만 남음). 기존 창이 안 나올 수 있다.
  * `ASFW_ANY`는 "아무 프로세스나" 잠시 포그라운드 권한을 얻게 한다. 특정 PID로 좁히려면 `GetNamedPipeServerProcessId`가 필요해 범위가
    커져 미뤘다.
  * `activate`는 파이프에 접근 가능한 로컬 프로세스 누구나 보낼 수 있지만 하는 일은 "메인 창 앞으로"뿐이다. `App.Mcp`는 이 타입을
    MCP 툴로 노출하지 않는다(툴 표면 최소화 원칙 유지).
  * 파이프 이름이 여전히 `StickyGhost.CharacterIpc`인데 이제 앱 제어 요청도 실어 나른다(이름은 역사적 이유로 유지).
* **롤백**
  * 이중 실행을 다시 허용하려면 `Program.cs`의 `singleInstance` 블록만 지우면 된다(C1-d가 있으므로 예전 같은 무한 루프는 안 난다).
    이 경우 두 인스턴스가 같은 SQLite 파일을 쓰고, 두 번째의 IPC 서버는 백오프하며 로그만 남긴다.
  * C1-d를 예전(`catch { }`)으로 되돌리는 것은 **권장하지 않는다** — 무한 루프가 재현된다.

## C2. 손상된 캐릭터 팩 이미지 → 기동 시 크래시

**증상(코드 읽기 기준)**: 로더는 이미지 파일이 "존재하는지"만 검증한다. PNG가 손상되어 `new Bitmap`이 예외를 던지면
`ApplyCharacterSettings`(`Opened` 핸들러)나 `OnSettingsClick`(async void)에서 프로세스가 죽고, 선택된 팩 id가 설정에 남아 있어
**매 실행마다** 죽을 수 있었다. 문서의 "로딩 실패 시 내장 팩 폴백"은 매니페스트 검증 실패만 다뤘다.

| 변경 | 파일 | 내용 |
|---|---|---|
| C2-a 트랜잭션형 `SetPack` | `CharacterWindow.axaml.cs` | 새 팩의 비트맵/알파 마스크를 **지역 변수로 먼저 전부** 만들고, 하나라도 실패하면 지역 자원만 Dispose하고 rethrow → 기존 상태(이전 팩)는 그대로. 성공한 뒤에야 기존 자원을 해제하고 교체 |
| C2-b 생성자 실패 정리 | `CharacterWindow` 생성자 | `SetPack`이 실패하면 미리 만들어 둔 말풍선 창(`_bubble`)과 `_windowCts`를 정리하고 rethrow(호출부가 참조를 못 가져 Closed가 안 오므로) |
| C2-c 폴백 | `CharacterOverlayController.Apply` | 렌더링 예외 시 내장 팩으로 재시도. 반환값 `PackApplyResult(UsedFallback, FailureReason)`. 내장 팩까지 실패하면 `InvalidOperationException`(배포 버그) |
| C2-d 알림/예외 범위 | `MainWindow.axaml.cs` | `ApplyCharacterSettings`가 `UsedFallback`이면 `ConfirmDialog`로 사유 안내. catch를 `InvalidOperationException`→`Exception` 전체로 확대. `OnSettingsClick` 전체를 try/catch로 감쌈 |

* **부수 효과로 해소된 것**: `CharacterPackLoadOutcome.UsedFallback`이 어디서도 안 쓰이던 문제(설계 의도였던 "호출부가 알림")의 렌더링 단계 버전.
* **트레이드오프**
  * **알림 다이얼로그가 기동 때마다 뜰 수 있다.** 선택된 팩 id(`SelectedCharacterPackId`)를 초기화하지 않기 때문 — 이용자가 팩을 고치면
    다시 정상 표시되게 하려는 의도. 거슬리면 `MainWindow.ApplyCharacterSettings`의 `if (result.UsedFallback)` 블록을 로그만 남기게 바꾸거나,
    폴백 시 설정의 팩 id를 지우면 된다.
  * `SetPack` 교체 순간 **이전 팩과 새 팩의 비트맵이 잠깐 동시에 메모리에 있다**(피크가 최대 2배). 큰 서드파티 팩에서는 점검 항목
    M1(팩 이미지 상주/크기 상한 없음)과 겹쳐 더 눈에 띌 수 있다.
  * 폴백은 "렌더링 예외"만 본다. 매니페스트 오류는 기존대로 `CharacterPackService`/스캐너가 처리한다.
  * 폴백 후 `CharacterPackService.CurrentPack`은 `LoadPack(내장 팩)`이 내장 팩으로 다시 맞춘다. 단, **내장 팩 폴백마저 실패하면** 창은 이전
    팩을 그리는데 `CurrentPack`은 내장 팩인 불일치가 남는다(배포 버그 상황이라 감수).
* **롤백**: C2-a를 되돌리면 손상된 이미지에서 창이 반쯤 빈 상태가 되고, C2-c/d를 되돌리면 다시 크래시한다. 다이얼로그만 없애려면 C2-d의
  `if (result.UsedFallback)` 블록만 지운다.

## C3. LLM 반응 호출의 미분류 예외 → 프로세스 종료

**증상(코드 읽기 기준)**: `CharacterWindow.HandleTouchEvent`가 async void이고 `OperationCanceledException`만 잡았다. 어댑터가 분류하지 못한
예외가 나가면 프로세스가 죽는다. 후보: Gemini `LooksLikeApiKeyErrorAsync`의 `KeyNotFoundException`(400 본문에 `error`가 없을 때),
키에 개행이 섞였을 때 `Headers.Add`의 `FormatException`, 시크릿 파일 IO 예외.

| 변경 | 파일 | 내용 |
|---|---|---|
| C3-a 원인 수정 | `GeminiChatCompletionAdapter.cs` | `LooksLikeApiKeyErrorAsync`가 `KeyNotFoundException`/`InvalidOperationException`도 `false`로 처리(`ParseResponse`와 같은 범위) |
| C3-b 원인 수정 | `GeminiChatCompletionAdapter.cs`, `OpenAiChatCompletionAdapter.cs` | `BuildHttpRequest`의 `FormatException` → `LlmFailure.Unauthorized`(헤더에 못 넣는 키 = 사실상 잘못된 키). **이 예외는 로깅하지 않는다**(메시지에 키가 들어 있음) |
| C3-c 원인 수정 | `SettingsWindow.axaml.cs` | API 키 저장 전 `Trim()` |
| C3-d 안전망 1 | `CharacterReactionService.cs`, `ICharacterLlmAdapter.cs` | `ReactAsync`가 취소 외 모든 예외를 잡아 로그 + `LlmFailure.Unexpected` 결과와 폴백 대사("앗, 뭔가 잘못된 것 같아...") 반환. **`LlmFailure.Unexpected` 신규** |
| C3-e 안전망 2 | `CharacterWindow.axaml.cs` | `HandleTouchEvent`에 `catch (Exception)` → 로그(표정/말풍선 반영 등 UI 쪽 예외용) |

* **설계 변경 사항**: `LlmFailure` enum에 `Unexpected`가 늘었다. 어댑터가 직접 반환하는 값이 아니라 오케스트레이터가 채우는 값이다
  (`character-widget-design.md`의 enum 정의도 갱신함).
* **트레이드오프**: 진짜 버그(예: 널 참조)도 "폴백 대사"로 가려져 겉보기엔 API 키 문제처럼 보일 수 있다. 캐릭터가 계속
  "앗, 뭔가 잘못된 것 같아..."만 말하면 `app.log`의 `[llm]`을 본다.
* **롤백**: C3-d를 지우면 다시 예외가 async void로 새어 나간다. C3-a~c는 개별로 되돌려도 서로 영향이 없다.

## G. 전역 예외 처리

| 변경 | 파일 | 내용 |
|---|---|---|
| G-a UI 스레드 | `App.UI/App.axaml.cs` | `Dispatcher.UIThread.UnhandledException` → 로그 + `e.Handled = true`. async void 등에서 새는 예외가 프로세스를 죽이지 않는다 |
| G-b 그 외 | `App.Windows/Program.cs` | `AppDomain.UnhandledException`(로그만, 종료는 못 막음), `TaskScheduler.UnobservedTaskException`(로그 + `SetObserved`) |

* **트레이드오프(중요)**: `Handled = true`로 삼킨 뒤에는 **예외 시점 이후의 상태가 어긋난 채로 계속 돌 수 있다**(예: 절반만 갱신된 UI).
  증상이 "이상하게 동작하는데 죽지는 않음"이면 `app.log`의 `[ui-unhandled]`를 먼저 본다. 사용자에게는 아무것도 보이지 않는다.
* **롤백**: `App.axaml.cs`의 `e.Handled = true;` 한 줄만 지우면 예전 동작(예외 시 종료, 단 로그는 남음)으로 돌아간다.

## 확인 방법 (사용자 실측 필요)

빌드 출력 경로: `src\App.Windows\bin\Debug\net8.0-windows\`.

1. **이중 실행 / 기존 창 앞으로 (C1)**: 앱 실행 → 메인 창을 최소화하거나 다른 창 뒤로 → 같은 `App.Windows.exe`를 다시 실행 →
   메인 창이 복원되어 앞으로 나오고, 두 번째 프로세스는 곧 사라져야 한다(작업 관리자로 `App.Windows` 프로세스가 하나뿐인지 확인).
   앞으로 안 나오고 작업표시줄만 깜박이면 포그라운드 권한(C1-c) 문제다.
2. **손상 팩 폴백 (C2)**: 위 경로의 `CharacterPacks\`에 `default` 폴더를 복사해 `broken`으로 만들고 `manifest.json`의 `id`를 `broken`으로
   바꾼 뒤 `mainimage.png`를 텍스트 파일로 교체(파일은 있으나 PNG 아님) → 앱 실행 → 설정에서 `broken` 선택 후 저장 →
   기본 캐릭터가 표시되고 안내 다이얼로그가 떠야 하며 `app.log`에 `[pack]`이 남아야 한다. 재실행해도 크래시 없이 같은 안내가 뜬다.
3. **반응 실패 폴백 (C3)**: 네트워크를 끊고 캐릭터를 찌르면 폴백 대사가 나와야 한다(기존 동작). `Unexpected` 경로 자체는 재현이 어려워
   코드 리뷰 수준으로만 확인했다.
4. **로그**: `%LocalAppData%\StickyGhost\logs\app.log` 생성 여부와 내용.

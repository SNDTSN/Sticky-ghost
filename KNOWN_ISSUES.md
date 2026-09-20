# Known Issues

지금 당장 고칠 만큼 급하진 않지만, 나중에 규모가 커지거나 다른 작업을 하다가 다시 마주칠 수 있어서
추적해두는 문제들을 모아둔 문서. 새로 발견한 게 있으면 여기 추가한다.

## 1. 효율성 — 사소한 변경마다 전체 목록을 통째로 재조회/재구성 (대부분 해결, 잔여 있음)

**해결됨 (`064785c`, 2026-09-20)**:
- `SqliteTodoRepository.GetIncomplete()`/`GetActiveWithDueDate()`의 체크리스트 N+1 쿼리를 `TodoItemId IN (...)` 한 번의
  배치 쿼리(`LoadChecklistItemsForMany`)로 교체.
- `MainViewModel`의 `AddTodo`/`CompleteTodo`/`DeleteTodoAsync`/`ToggleChecklistItem`이 매번 `LoadItems()`로 전체를 재구성하던 것을
  `UpsertItem`/`RemoveItem`/`RefreshItem`으로 바꿔 변경된 항목 하나만 반영.

**잔여 (지금은 체감되지 않음 — 항목이 수십 개 수준, SQLite 로컬 파일)**:
- 카테고리 이름/색 수정은 여전히 전체 재구성이다. `CategoryViewModel.OnNameChanged`/`OnColorHexChanged`가 `onEdited`(= `MainViewModel.LoadItems`)를
  호출하는데, TextBox/ColorPicker 바인딩이 글자 하나/드래그 한 틱마다 갱신하므로 입력마다 Save + `GetAll` + `GetIncomplete` + ViewModel 전량 재생성이 돈다.
- `UpsertItem`이 호출될 때마다 `_categoryRepository.GetAll()`을 다시 조회한다.
- 메모 색상 변경(`MemoNoteViewModel.OnColorHexChanged`)도 ColorPicker를 드래그하는 동안 틱마다 즉시 DB에 Save한다(디바운스 없음 —
  `docs/memo-design.md` 의사코드가 "색상 변경 시 Save"로 되어 있어 그대로 구현).
- `TodoService.CheckDueSoon`은 알림을 보낼 항목마다 별도 커넥션으로 `Save`를 호출한다. 지금은 호출하는 스케줄러가 없어 실행되지 않는다(→ #15).

**개선 방향 (나중에 항목 수가 늘어나면)**:
- 카테고리 편집은 해당 카테고리를 쓰는 `TodoItemViewModel`만 갱신하거나, 입력을 디바운스(메모 내용 저장과 같은 방식)한다.
- 색상 변경 저장에 디바운스를 건다.

## 2. `SqliteSchemaInitializer` — 실제 스키마 마이그레이션 메커니즘이 없음 (부분 해결)

**어디**: `App.Core/Infrastructure/Sqlite/SqliteSchemaInitializer.cs`.

**증상**: `CREATE TABLE IF NOT EXISTS`로만 스키마를 만든다. 테이블이 이미 존재하면 그 안의 컬럼 구성은
전혀 안 건드린다. `TodoItem.CompletionCount` 컬럼을 추가한 뒤 실제로 이 문제가 터졌다 — 컬럼 추가 전
스키마로 이미 만들어져 있던 로컬 DB로 앱을 실행하니 `CREATE TABLE IF NOT EXISTS`가 아무 일도 안 해서
새 컬럼이 반영되지 않았고, `SqliteTodoRepository.ReadTodoItem`이 `CompletionCount`를 읽으려다
`ArgumentOutOfRangeException`으로 앱이 시작하자마자 크래시했다.

**적용한 임시 해결**: `EnsureColumnExists(connection, table, column, columnDefinition)` 헬퍼를 추가해서,
`PRAGMA table_info(table)`로 컬럼 존재 여부를 확인하고 없으면 `ALTER TABLE ... ADD COLUMN`으로 보충한다.
`EnsureCreated`가 테이블 생성 직후 `TodoItem.CompletionCount`에 대해 이 헬퍼를 호출하도록 배선함 — 기존
로컬 DB(할 일/카테고리/메모 데이터 보존됨)로도 앱이 정상 기동되는 것까지 확인 완료.

**아직 안 풀린 부분**: 이 헬퍼는 "컬럼 하나 추가"만 커버한다. 컬럼 삭제/이름 변경/타입 변경처럼 `ALTER TABLE
ADD COLUMN`으로 안 되는 구조 변경, 또는 앞으로 추가될 컬럼마다 `EnsureCreated`에 호출을 일일이 늘어놓는 방식
자체의 확장성 문제는 그대로 남아 있다. `PRAGMA user_version` 기반의 정식 버전 관리 마이그레이션은 실제
배포가 시작되기 전에 넣어야 함 — 배포 이후 구조적 스키마 변경이 필요해지는 **첫 순간부터는 반드시** 해결하고
넘어가야 함.

## 3. ~~메모 위젯 — 새 메모 캐스케이드 위치 미구현~~ — 해결됨 (`064785c`, 2026-09-20)

`MainWindow.NextMemoCascadePosition`이 구현되어 있다: 이번 실행의 첫 메모는 메인 창 바로 옆(왼쪽 → 오른쪽 순으로 통째로 들어갈 자리가 있는 쪽)에서
시작하고, 이후는 +24px 캐스케이드, 작업 영역을 벗어나면 (100,100)에서 다시 시작한다. 규칙은 `docs/memo-design.md`의 "창 위치 복원 보강" 참고.

## 4. ~~`HexColorToBrushConverter` — 변환마다 새 Brush 할당~~ — 해결됨 (`064785c`, 2026-09-20, 사소한 잔여)

`Dictionary<string, IBrush>`로 같은 색상 문자열의 Brush를 캐싱한다. **잔여**: 캐시에 상한이 없다. 컨버터는 창별 `Window.Resources` 인스턴스라
창을 닫으면 사라지지만, 메모 창에서 ColorPicker를 드래그하는 동안 서로 다른 hex마다 Brush가 하나씩 창 수명 동안 쌓인다(긴 세션에서도 KB 수준이라
지금은 무시). 필요해지면 항목 수가 일정 개수를 넘을 때 비우는 정도로 충분하다.

## 5. LLM 어댑터 자극 설명 템플릿 — 한국어 하드코딩

**어디**: `App.Core/Domain/Services/CharacterReactionService.cs`의 `DescribeTodoEvent`/`DescribeKind`
같은 자극→텍스트 템플릿.

**증상**: TouchEvent/TodoEvent를 LLM에 넘길 자극 설명 문장으로 바꾸는 템플릿이 서비스 내부에 한국어
고정 문자열로 박혀 있다.

**왜 지금은 안 심각한가**: `design-draft.md`에 명시된 대로 지금은 한국인 사용자만 대상으로 하고 있어서
당장 문제되지 않는다.

**개선 방향**: 나중에 다국어 지원이 들어가면 이 템플릿들을 리소스 파일 등으로 분리해야 한다. 지금은
설계 범위 밖.

## 6. ~~`App.UI` — `App.Platform.Stub` 구체 구현을 직접 참조 (임시 배선)~~ — 해결됨 (2026-09-20)

`IWindowBehavior`를 `MainWindow` 생성자 주입으로 바꾸고 `App.WindowBehaviorFactory`(실행 진입점 `App.Windows/Program.cs`가
`WindowsWindowBehavior`를 조립)를 추가했다. `App.UI.csproj`의 `App.Platform.Stub` 참조와 `new StubWindowBehavior()`는 제거.
(Stub 프로젝트 자체는 Mac 이식/테스트용으로 유지 — 남은 임시물은 DI 컨테이너 없이 `MainWindow`가 손으로 조립하는 부분.)

## 7. ~~메모 위젯 📌(항상 위 표시) 버튼이 실제로는 동작하지 않음~~ — 코드상 해결됨 (2026-09-20, 실행 확인 대기)

원인은 `IWindowBehavior`가 no-op Stub이었던 것. `WindowsWindowBehavior.SetAlwaysOnTop`(`SetWindowPos(HWND_TOPMOST)`)을 구현해
주입했다. **실제로 📌 토글 시 다른 창 위에 고정되는지는 사용자 확인 필요.**

## 8. 캐릭터 오버레이 창 — 투명 영역이 뒤에 있는 창 클릭을 막음 → `SetInputShape`로 대응 (실행 확인 대기)

v1의 사각형 히트박스는 실제로 불편했다(설정 버튼이 캐릭터의 투명 부분에 가려져 눌리지 않음). `IWindowBehavior.SetInputShape`
(`SetWindowRgn`)로 창 모양을 불투명 픽셀(알파 16 이상)의 합집합으로 제한하도록 구현했다. 표정 이미지는 base의 투명 영역에 걸쳐
있을 수 있어서 떠 있는 동안만 포함한다. **Avalonia 투명 창에서 `SetWindowRgn`이 실제로 렌더링/입력을 자르는지는 실측 필요** —
안 먹는다면 폴백은 `GetCursorPos` 폴링으로 `WS_EX_TRANSPARENT`를 토글하는 방식.

## 9. 캐릭터 팩 렌더링 실패 안내 다이얼로그가 기동 때마다 뜰 수 있음 (2026-09-20)

**어디**: `App.UI/Views/MainWindow.axaml.cs`의 `ApplyCharacterSettings`.

**증상**: 선택한 팩의 이미지가 손상되어 내장 팩으로 폴백하면 안내 `ConfirmDialog`를 띄운다. 설정의 `SelectedCharacterPackId`는
그대로 남기 때문에(팩을 고치면 다시 정상 표시되게 하려는 의도) 팩을 고치거나 다른 팩을 고르기 전까지 **실행할 때마다** 뜬다.

**개선 방향**: 기동 시에는 로그만 남기고 설정창을 닫은 직후에만 띄우기, 또는 폴백 시 설정의 팩 id를 지우기. 상세: `docs/stability-hardening.md` "C2".

**참고**: 이 알림은 렌더링(이미지 디코딩) 실패용이다. 매니페스트 검증 실패 시 `CharacterPackService`가 내장 팩으로 폴백했다는 `UsedFallback`은 여전히
호출부가 쓰지 않는다 — 다만 스캐너가 검증을 통과한 팩만 목록에 올리므로 실제로 그 경로에 닿는 경우는 드물다(선택한 팩이 스캔 뒤에 망가진 경우 등).

## 10. UI 미처리 예외를 삼키고 계속 실행 — 예외 이후 상태가 어긋날 수 있음 (2026-09-20)

**어디**: `App.UI/App.axaml.cs`의 `Dispatcher.UIThread.UnhandledException` 핸들러(`e.Handled = true`).

**증상**: 예외를 로그(`%LocalAppData%\StickyGhost\logs\app.log`의 `[ui-unhandled]`)에만 남기고 앱은 계속 돈다. 사용자에게는 아무 알림이
없고, 예외 시점 이후 UI/상태가 절반만 갱신된 채 남을 수 있다. "이상하게 동작하는데 죽지는 않는다"면 이 로그를 먼저 본다.

**되돌리는 법**: `e.Handled = true;` 한 줄 삭제(예외 시 종료 — 로그는 남음). 상세: `docs/stability-hardening.md` "G".

## 11. 캐릭터 팩 id가 중복되면 팩 선택이 동작하지 않음 (2026-09-20, 손대지 않기로 함)

**어디**: `App.Core/Domain/Services/CharacterPackScanner.cs`(id 유일성 검사 없음), `App.UI/Services/CharacterOverlayController.cs`의 `Apply`
(선택 id로 스캔 결과의 **첫 항목**을 고르고, 현재 창의 `PackId`와 id가 같으면 `SetPack`을 건너뜀), `App.UI/Views/SettingsWindow.axaml.cs`
(선택값을 폴더가 아니라 `SelectedCharacterPackId`로 저장).

**증상**: 서로 다른 폴더의 팩이 같은 `manifest.json`의 `id`를 가지면(예: 기본 팩 `default` 폴더를 복사해서 `id`를 안 바꾼 채 고쳐 쓴 팩)
설정창에서 어느 쪽을 골라도 저장되는 값이 같아서 **아무 변화가 없다.** 스캔 순서상 먼저 나오는 폴더(보통 이름순)가 항상 선택되고, 그 팩의
id가 현재 표시 중인 팩의 id와 같으면 교체 자체를 건너뛰어 예외도 로그도 남지 않는다. 설정창을 열 때 콤보박스도 항상 첫 항목으로 보인다.
실제로 안정성 보강 실측 중(손상 팩 폴더를 `default`에서 복사해 `id`를 바꾸지 않았을 때) 발생했다 — `docs/stability-hardening.md` 참고.

**왜 지금은 손대지 않는가**: 팩 제작자가 `id`를 바꾸는 것을 전제로 한 설계(`docs/character-widget-design.md`: `id`는 폴더명과 별개로 명시하는
팩 고유 식별자)라서, 당장은 문서/안내로 감당하기로 함. 다만 "폴더를 복사해서 고쳐 쓰는" 것이 모딩의 기본 흐름이라 제작자가 겪을 가능성은 높다.

**우회**: 복사한 팩의 `manifest.json`에서 `id`를 다른 값으로 바꾼다.

**개선 방향 (나중에)**:
- `CharacterPackScanner`가 id 중복을 걸러낸다: 내장 팩을 우선하고 나머지 중복은 목록에서 제외하며 `AppLog`에 기록. 설정창 안내 문구에
  "id가 중복되어 제외된 폴더"를 표시한다(그러면 내장 `default` id를 복사한 팩이 내장 팩을 가로채지도 못한다).
- 또는 선택값을 id가 아니라 폴더로 저장해 중복 id도 둘 다 고를 수 있게 한다(설정 스키마 변경 필요).
- 어느 쪽이든 로더 규칙(`docs/character-widget-design.md`)에 "id는 팩 간에 유일해야 한다"를 명시한다.

**참고**: 안정성 보강(`ad36343`)의 회귀가 아니다 — 스캐너와 `Apply`의 id 비교는 그 변경에서 건드리지 않았다.

## 12. 캐릭터 팩 이미지를 전부 상주시키고 크기 상한이 없음 — 큰 서드파티 팩에서 메모리 폭증 (2026-09-20 점검)

**어디**: `App.UI/Views/CharacterWindow.axaml.cs`의 `SetPack`(base/눈감김/모든 표정을 디코딩해 창 수명 동안 보관 + 레이어별 알파 마스크 보관),
`App.Core/Infrastructure/FileSystem/JsonCharacterPackLoader.cs`의 `ValidateImagePath`(파일 존재만 확인, 크기/디코딩 가능 여부는 안 봄).

**증상**: 이미지 크기 상한이 없어서 큰 PNG를 쓰는 팩은 메모리를 많이 먹는다. 2000×3000 PNG는 레이어당 약 30MB(디코드 24MB + 알파 마스크 6MB)이고
base + 눈감김 + 표정 10개면 약 360MB다. 마스크를 뽑는 동안 같은 크기의 임시 `WriteableBitmap`이 잠깐 더 필요하고, 팩 교체 순간에는(안정성 보강의
트랜잭션형 `SetPack`) 이전 팩과 새 팩이 잠깐 공존해 피크가 최대 2배가 된다. 예제 팩(304×324)은 레이어 7개에 3MB대라 지금은 드러나지 않는다.

**왜 지금은 안 급한가**: 배포 생태계가 없고 예제 팩만 있다. 다만 "이용자가 팩을 직접 만들어 배포한다"는 프로젝트 방향상 언젠가 반드시 만난다.

**개선 방향**: (1) 로더/스캐너에서 PNG 헤더(IHDR 24바이트)만 읽어 가로·세로 상한을 검증(전체 디코딩 불필요). (2) 표정 비트맵/마스크를 지연 로딩하고 LRU로 보관
(눈감김/현재 표정만 상주). 지연 로딩은 표정 전환 시 디코딩 지연이 생기므로 미리 예열하는 방안과 함께 검토.

## 13. `ApplyInputShape`가 호출될 때마다 큰 배열을 새로 할당 — LOH 쓰레기 (2026-09-20 점검, 사소한 항목 포함)

**어디**: `App.UI/Services/InputShapeBuilder.cs`의 `Build`(`new bool[dstWidth * dstHeight]`), 호출부 `CharacterWindow.ApplyInputShape`.

**증상**: 100% 배율의 예제 팩도 배열이 98,496바이트로 LOH 기준(85,000바이트)을 넘는다. `ApplyInputShape`는 표정 반응 1회당 두 번(표시, 1.5초 뒤 해제),
그리고 창 크기/배율/DPI가 바뀔 때마다 호출되어 gen2 GC 전까지 회수되지 않는 쓰레기가 쌓인다(배율 200% + DPI 150%면 호출당 약 1MB). 진짜 누수는 아니고
상주형 앱의 작업 집합이 서서히 올라가는 형태다.

**개선 방향**: 결과 사각형 목록을 `(현재 표정 id, 창 물리 크기, factor)` 키로 캐시하고 팩/배율/DPI가 바뀔 때만 무효화하면 반복 반응에서 할당이 0이 된다.
버퍼 재사용(`ArrayPool<bool>` 등)만으로도 LOH 할당은 사라진다.

**같은 결의 사소한 것**: `CharacterWindow.ScheduleNextBlink`/`PlayBlink`가 깜빡임(2~6초 간격)마다 `DispatcherTimer`와 클로저를 새로 만든다. `Stop()`은 하므로 누수는
아니고 gen0 부담뿐이다. 타이머 하나를 만들어 `Interval`만 바꿔 쓰면 된다.

## 14. IPC 서버가 연결당 타임아웃/라인 길이 상한이 없음 (2026-09-20 점검)

**어디**: `App.Core/Infrastructure/Ipc/CharacterIpcServer.cs`의 `HandleOneConnectionAsync`(`ReadLineAsync`에 시간·길이 제한 없음), 클라이언트
`App.Mcp/Tools/CharacterTools.cs`(`CancellationToken.None`).

**증상**: 서버는 연결을 하나씩 순서대로 처리한다. 연결만 하고 줄을 안 보내는 로컬 프로세스가 있으면 그 연결이 끝날 때까지 다른 요청이 전부 막히고
(클라이언트에는 "위젯이 실행 중이지 않습니다"로 잘못 보임), 개행 없이 계속 보내면 줄 버퍼가 끝없이 커진다. 클라이언트의 응답 대기에도 타임아웃이 없다.

**왜 지금은 안 급한가**: 로컬 전용 파이프이고 정상 클라이언트(`App.Mcp`)는 즉시 응답을 받는다. 현실적인 위험은 낮다.

**개선 방향**: 연결당 `CancelAfter`(예: 5초)를 건 링크 토큰과 줄 길이 상한(예: 8KB). 클라이언트 응답 대기에도 같은 타임아웃.

## 15. To-do 알림 설계 결함 — 이벤트 버스를 캐릭터에 연결하기 **전에** 손볼 것 (2026-09-20 점검)

**어디**: `App.Core/Domain/Services/TodoService.cs`의 `CheckDueSoon`(`docs/todo-design.md` "핵심 흐름" 의사코드와 동일한 구조), `MainWindow`의 `NoOpTodoEventBus`.

**현재 상태**: 아무도 `CheckDueSoon`을 부르지 않고(스케줄러 없음), 이벤트 버스는 구독자 없는 `NoOpTodoEventBus`이며, `CharacterReactionService.ReactToTodoAsync`도
호출자가 없다. 즉 "할 일 완료/마감 임박에 캐릭터가 반응"은 아직 동작하지 않는다(문서의 미체크 항목과 일치). 연결할 때 아래 결함이 그대로 드러난다.

**결함**:
- 마감이 이미 지났는데 한 번도 알리지 않은 항목은 첫 분기(`minutesLeft <= threshold && !NotifiedDueSoon`)에 걸려 `TodoDueSoon(minutesLeft = 음수)`로 나간다.
  LLM 자극 문장이 "마감이 -300분 남았는데"가 된다.
- `TodoOverdue`는 중복 방지 플래그가 없어 검사할 때마다(1분 주기 가정) 계속 발행된다. LLM에 연결하면 매분 API 호출 = 비용이다.

**개선 방향**: `dueDate < now` 분기를 먼저 검사하고, 초과 알림용 `NotifiedOverdue` 플래그를 추가한다(컬럼 추가는 #2의 `EnsureColumnExists` 방식으로 가능).
또는 이벤트를 받는 쪽에서 (항목 id, 종류) 단위로 디바운스한다. `docs/todo-design.md`의 의사코드도 함께 고친다.

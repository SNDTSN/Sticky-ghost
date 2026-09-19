# To-do List 설계

`docs/design-draft.md`의 "To-do list" 항목을 구체화한 문서. 이 프로젝트의 코어 기능이므로 캐릭터/메모 위젯보다 먼저 설계를 확정한다.

## 데이터 모델

```
Entity TodoItem
    Id: Guid
    Title: string
    CategoryId: Guid?           // Category 참조, null이면 "미분류"
    IsImportant: bool
    IsUrgent: bool
    DueDate: DateTime?
    IsCompleted: bool
    CreatedAt: DateTime
    CompletedAt: DateTime?
    ChecklistItems: List<ChecklistItem>
    Recurrence: RecurrenceRule?   // null이면 1회성 항목
    NotifiedDueSoon: bool         // 마감 임박 알림 중복 발행 방지
    CompletionCount: int          // 지금까지 완료된 누적 횟수. 0이면 한 번도 완료된 적 없음.
                                   // 반복 항목은 완료 즉시 IsCompleted/CompletedAt이 다음 회차용으로 리셋되어
                                   // "최초 상태"와 구분이 안 되기 때문에 별도로 둠

Entity ChecklistItem
    Id: Guid
    TodoItemId: Guid
    Text: string
    IsChecked: bool
    SortOrder: int

Entity Category
    Id: Guid
    Name: string
    Color: string (hex)
    SortOrder: int

Value RecurrenceRule
    Type: enum(Daily, Weekly, Monthly)
    Interval: int                // N일/주/달마다
    DaysOfWeek: Set<DayOfWeek>?   // Weekly일 때만 사용
    EndDate: DateTime?            // null이면 무기한 반복
```

### RecurrenceRule.ComputeNext 구현 (확정, `src/App.Core/Domain/Entities/RecurrenceRule.cs`)

* Daily: `currentDueDate.AddDays(Interval)`
* Monthly: `currentDueDate.AddMonths(Interval)` (.NET이 말일 초과를 자동으로 그 달의 마지막 날로 내려줌)
* Weekly:
  * `DaysOfWeek`가 없으면 `currentDueDate.AddDays(7 * Interval)`
  * `DaysOfWeek`가 있으면 현재 날짜 다음으로 오는 지정 요일을 순회로 찾음 (최대 7일 탐색)
* **MVP 제한**: `DaysOfWeek`가 설정된 경우 `Interval`은 반드시 1 (생성자에서 검증, 위반 시 `ArgumentException`). "격주로 월/수/금" 같은 조합은 anchor 주 개념이 필요해서 실제 수요가 생기기 전까지는 지원하지 않음. 막히는 건 "여러 요일 동시 지정 + 2주 이상 간격" 조합뿐이고, "N주마다 요일 하나"는 `DaysOfWeek` 없이 `Interval`만으로 이미 가능.
* `Recurrence != null`인데 `DueDate == null`인 상태를 막는 불변식은 `RecurrenceRule` 자체가 아니라 `TodoService.AddTodo`(다음 단계 작업)에서 검증하기로 함 — `ComputeNext`는 non-null `DateTime`만 받는다고 가정.

### 중요도 — 아이젠하워 매트릭스

`Importance` 단일 enum 대신 `IsImportant` / `IsUrgent` 두 개의 독립 boolean 축으로 결정. UI에서 2x2 매트릭스로 보여주기 쉽고, 축 하나만으로 필터링이 가능하다.

```
computed Quadrant =
    (IsImportant, IsUrgent) match
    | (true,  true)  -> "중요&긴급"         // 즉시 처리
    | (false, true)  -> "긴급하지만 안중요"   // 위임/빠른 처리
    | (true,  false) -> "중요하지만 안급함"   // 계획해서 처리
    | (false, false) -> "안중요&안급함"       // 나중에/삭제 검토
```

`IsUrgent`는 `DueDate` 근접도로 자동 계산하지 않고 수동 필드로 둔다. 마감이 멀어도 사용자가 "급한 일"로 직접 지정할 수 있어야 하기 때문. 추후 "마감 임박 시 자동으로 IsUrgent 제안" 정도의 보조 기능만 얹는 것을 고려.

## 반복 일정 처리 방식

두 가지 방식을 검토함:

- **A. 롤오버** — 완료 시 같은 row의 `DueDate`를 다음 회차로 굴림. 테이블 크기가 작게 유지되고 조회가 단순하지만, 회차별 상세 이력(그날 체크리스트 상태 등)이 사라짐.
- **B. 회차별 row 생성** — 반복 항목마다 실제 row를 생성. 회차별 상세 이력이 온전히 보존되지만, 무기한 반복 항목은 row가 끝없이 쌓여 DB 크기·조회 성능에 영향을 줄 수 있고, 반복 규칙 수정 시 "이 회차만 vs 이후 전체" UX 분기가 필요해짐.

**결정: A + 절충안.** 롤오버를 기본으로 하되, 완료 시 `CompletionLog`에 체크리스트 상태를 JSON 스냅샷으로 남겨 상세 이력 손실을 보완한다. 실제 사용해보고 불편하면 B안 전환을 재검토한다.

## 캐릭터 알림 훅

to-do 모듈은 캐릭터 존재 여부를 몰라도 되도록 이벤트 버스로 완전히 분리한다. 캐릭터 엔진/MCP 모듈은 이 버스를 구독만 하면 됨 (구독 측 구현은 캐릭터 엔진 설계 단계에서 다룸).

```
interface ITodoEventBus
    Publish(event: TodoEvent)

TodoEvent =
    | TodoCreated(item)
    | TodoCompleted(item)
    | TodoDueSoon(item, minutesLeft)   // 백그라운드 스케줄러가 주기적으로 검사해서 발행
    | TodoOverdue(item)
```

**구현 완료** (`src/App.Core/Domain/Events/`): `ITodoEventBus`와 `TodoEvent`는 C# record 계층구조로 옮김 (`abstract record TodoEvent` + `sealed record TodoCreated/TodoCompleted/TodoDueSoon/TodoOverdue`). 구독 측에서 `switch` 패턴 매칭으로 전수 검사가 가능해서 선택.

**주의 — `Item`은 `TodoItem`이 아니라 `TodoItemSnapshot`**: `TodoItem`은 참조 타입(mutable class)이라, 이벤트에 그대로 담아 넘기면 구독자가 나중에(비동기로) 읽는 시점엔 이미 다른 값으로 바뀌어 있을 수 있다. 특히 반복 항목은 `CompleteTodo` 안에서 완료 처리 직후 다음 회차로 롤오버되는데, 이벤트를 실시간으로 동기 처리하지 않고 캐릭터/MCP 쪽이 나중에 폴링해서 읽는 구조(`design-draft.md`의 Claude "수동 반응" 모델)에서는 `TodoCompleted.Item`이 "방금 완료된 회차"가 아니라 "이미 롤오버된 다음 회차"를 보여주는 문제가 있었다. 그래서 `TodoEvent`의 `Item`은 발행 직전에 `TodoItemSnapshot.From(item)`으로 뜬 불변 스냅샷(`src/App.Core/Domain/Events/TodoItemSnapshot.cs`)을 쓴다 — `CompleteTodo`는 롤오버 전에 스냅샷을 떠서 `TodoCompleted`에 담고, 그 다음에 롤오버 처리를 이어간다.

## SQLite 스키마 (DDL)

```sql
CREATE TABLE Category (
    Id          TEXT PRIMARY KEY,      -- GUID
    Name        TEXT NOT NULL,
    Color       TEXT NOT NULL,          -- hex
    SortOrder   INTEGER NOT NULL DEFAULT 0
);

CREATE TABLE TodoItem (
    Id                  TEXT PRIMARY KEY,
    Title               TEXT NOT NULL,
    CategoryId          TEXT REFERENCES Category(Id) ON DELETE SET NULL,
    IsImportant         INTEGER NOT NULL DEFAULT 0,   -- bool
    IsUrgent            INTEGER NOT NULL DEFAULT 0,   -- bool
    DueDate             TEXT,                          -- ISO8601, null 허용
    IsCompleted         INTEGER NOT NULL DEFAULT 0,
    CreatedAt           TEXT NOT NULL,
    CompletedAt          TEXT,

    -- 반복 규칙 (없으면 전부 NULL)
    RecurrenceType       TEXT,          -- 'Daily' | 'Weekly' | 'Monthly'
    RecurrenceInterval   INTEGER,       -- N일/주/달마다
    RecurrenceDaysOfWeek INTEGER,       -- Weekly용 비트마스크. 순번(1,2,3..)이 아니라 요일마다 2의 거듭제곱을 배정해 OR로 합침: 월=1,화=2,수=4,목=8,금=16,토=32,일=64 (예: 월+수+금 = 1|4|16 = 21)
    RecurrenceEndDate    TEXT,          -- null이면 무기한

    NotifiedDueSoon      INTEGER NOT NULL DEFAULT 0
);

CREATE TABLE ChecklistItem (
    Id          TEXT PRIMARY KEY,
    TodoItemId  TEXT NOT NULL REFERENCES TodoItem(Id) ON DELETE CASCADE,
    Text        TEXT NOT NULL,
    IsChecked   INTEGER NOT NULL DEFAULT 0,
    SortOrder   INTEGER NOT NULL DEFAULT 0
);

CREATE TABLE CompletionLog (
    Id                TEXT PRIMARY KEY,
    TodoItemId        TEXT NOT NULL REFERENCES TodoItem(Id) ON DELETE CASCADE,
    CompletedOn       TEXT NOT NULL,
    ChecklistSnapshot TEXT   -- json, 완료 당시 체크리스트 상태 (nullable)
);

CREATE INDEX idx_todo_duedate   ON TodoItem(DueDate) WHERE IsCompleted = 0;
CREATE INDEX idx_checklist_todo ON ChecklistItem(TodoItemId);
CREATE INDEX idx_log_todo       ON CompletionLog(TodoItemId);
```

**구현 완료** (`src/App.Core/Infrastructure/Sqlite/`): `SqliteSchemaInitializer`(위 DDL을 `CREATE TABLE IF NOT EXISTS`로 실행), `SqliteTodoRepository`, `SqliteCompletionLogStore`. 커넥션은 메서드 호출마다 새로 열고(Microsoft.Data.Sqlite 내장 풀링 활용), `Save(item)` 호출 시 `ChecklistItem`은 기존 행을 전부 지우고 현재 리스트를 재삽입하는 방식으로 단순화. "2주마다 금요일" 같은 실제 시나리오(`Weekly, Interval=2, DaysOfWeek 없음`)로 AddTodo → CompleteTodo → 재조회까지 임시 스모크 테스트로 검증함 (요일 유지, 체크리스트 초기화, RecurrenceRule 인코딩/디코딩 정상 확인).

## 핵심 흐름 (의사코드)

```
AddTodo(title, categoryId, isImportant, isUrgent, dueDate, checklist, recurrence):
    item = new TodoItem(...)
    repository.Save(item)
    eventBus.Publish(TodoCreated(item))

CompleteTodo(id):
    item = repository.Get(id)
    snapshot = json.Serialize(item.ChecklistItems)   // 완료 당시 상태 캡처

    item.IsCompleted = true
    item.CompletedAt = now
    completionLog.Append(id, now, snapshot)

    if item.Recurrence != null:
        item.DueDate = item.Recurrence.ComputeNext(item.DueDate)
        item.IsCompleted = false
        item.NotifiedDueSoon = false
        for checklistItem in item.ChecklistItems:
            checklistItem.IsChecked = false   // 다음 회차용으로 초기화

    repository.Save(item)
    eventBus.Publish(TodoCompleted(item))

DeleteTodo(id):
    repository.Delete(id)   // ChecklistItem/CompletionLog는 FK ON DELETE CASCADE로 같이 삭제됨

// 백그라운드 타이머 (예: 1분마다)
CheckDueSoon():
    for item in repository.GetActiveWithDueDate():
        if item.DueDate - now <= threshold and not item.NotifiedDueSoon:
            item.NotifiedDueSoon = true
            eventBus.Publish(TodoDueSoon(item, minutesLeft))
        elif item.DueDate < now:
            eventBus.Publish(TodoOverdue(item))
```

**구현 완료** (`src/App.Core/Domain/Services/TodoService.cs`): 위 의사코드를 그대로 옮기되, 구현하면서 확정한 것 세 가지.

* **`Recurrence.EndDate` 초과 처리**: `ComputeNext`로 계산한 다음 회차가 `EndDate`를 넘으면 더 굴리지 않고 `IsCompleted = true`인 채로 멈춤. `Recurrence` 필드 자체는 지우지 않아서 "예전엔 반복이었다"는 정보가 남음.
* **`IClock` 추상화 도입** (`src/App.Core/Domain/Services/IClock.cs` + `SystemClock.cs`): `DateTime.Now`를 직접 쓰지 않고 `IClock.Now`로 주입받아, 나중에 테스트에서 "지금 시각"을 고정할 수 있게 함.
  이 앱은 한국 사용자 전용이라 `Now`는 UTC가 아닌 로컬(KST) 시각을 반환한다 — 최초엔 `UtcNow`였다가, `DueDate`(로컬 시각)와 기준이 달라 `CheckDueSoon`의 마감 임박 판정이 9시간 밀리는 버그가 발견되어 로컬 시각으로 통일함. 상세 내용과 다중 시간대 지원 시 손봐야 할 지점 목록은 [docs/timezone-policy.md](./timezone-policy.md) 참고.
* **`AddTodo`에서 불변식 검증**: `Recurrence != null`인데 `dueDate == null`이면 `ArgumentException`. `RecurrenceRule.ComputeNext`가 non-null `DateTime`만 받는다고 가정하는 전제(위 섹션 참고)를 여기서 실제로 지킴.
* `CompleteTodo`가 존재하지 않는 `id`를 받으면 `InvalidOperationException`.
* **`DeleteTodo`는 뒤늦게 추가됨 (2026-09-19)**: 애초 설계엔 없었는데, `ITodoRepository`에 `Delete`가 아예 없어서
  반복 종료일이 없는 항목(=완료해도 계속 다음 회차로 롤오버되어 `GetIncomplete()`에서 절대 안 사라짐)을 지울
  방법이 전혀 없다는 게 UI 작업 중 드러남. 일반 항목은 완료 시 목록에서 빠지는 것으로 "삭제된 것처럼" 보였을
  뿐 실제로는 DB에 계속 남아있었던 셈 — 반복 없는 항목도 똑같이 삭제 수단이 없었음. `DeleteTodo`는 존재 여부
  검사 없이(=없는 `id`를 넘겨도 조용히 무시) 바로 지우는 멱등 동작으로 둠, `CompleteTodo`처럼 예외를 던지지 않음.

## 다음 단계

- [x] 엔티티(`TodoItem`/`ChecklistItem`/`Category`/`RecurrenceRule`) + 리포지토리·이벤트버스 인터페이스 작성 (`src/App.Core/Domain/`)
- [x] `TodoService` (AddTodo/CompleteTodo/CheckDueSoon 비즈니스 로직) — `IClock` 포함
- [x] `SqliteTodoRepository`/`SqliteCompletionLogStore` 등 이 문서의 DDL 기준 실제 SQLite 구현 (스모크 테스트로 검증 완료)
- [x] 메모 위젯 설계 (`docs/memo-design.md`) — 엔티티/리포지토리/SQLite/`App.UI` 뷰까지 전부 완료
- [x] `App.UI`에 To-do 리스트 1차 UI (`MainWindow`) — 단순 목록 + 제목/마감일/중요·긴급만 지원.
  체크리스트 입력, 반복 설정, 카테고리 지정, 아이젠하워 매트릭스 뷰는 다음 이터레이션으로 미룸.
  작업 중 `ITodoRepository.GetIncomplete()` 추가, `NoOpTodoEventBus`(캐릭터 엔진 붙기 전 임시 구현) 추가.
  버그 수정: `DatePicker.SelectedDate`(`DateTimeOffset`)를 `.UtcDateTime`으로 저장하면 UTC+9 등 양수 시간대에서 하루 당겨지는 문제 발견 — `.DateTime`으로 수정.
- [x] To-do 리스트 2차 — 체크리스트 입력/표시/토글 (`MainViewModel`, `TodoItemViewModel`, `ChecklistItemViewModel`). 실행 확인 완료.
  - **미결정 사항**: 체크리스트 하위 항목을 전부 체크했을 때 상위 `TodoItem`을 자동으로 완료 처리할지 여부 — 아직 결론 안 남, 코드에 반영하지 않음. 다음에 다룰 때 고려할 점: 자동 완료가 반복 항목(`Recurrence`)과 만나면 체크리스트도 매 회차 초기화되는 기존 로직과 상호작용이 생김, 그리고 사용자가 "일부러 체크리스트만 다 채우고 아직 완료로 안 넘기고 싶은" 경우와 충돌할 수 있음.
  - [x] 반복 설정 UI (`RecurrenceTypeOption`, `DayOfWeekOptionViewModel`) — 유형/간격/요일(1주 간격 제한 반영)/종료일 입력, 목록에 "🔁 매일 (종료 ...)" 형태 배지 표시. 완료 체크 시 실제로 다음 회차로 굴러가는 것까지 실행 확인 완료.
  - [x] 카테고리 CRUD + 지정 UI (`ICategoryRepository`/`SqliteCategoryRepository` 신규 추가, `CategoryViewModel`).
    칩 형태로 이름/색상 즉시 수정(Update), 삭제 시 `TodoItem.CategoryId`의 `ON DELETE SET NULL` FK로 자동 미분류 처리됨 — 실행 확인 완료.
- [x] To-do 리스트 3차 — UX 개편 (2026-09-19, `MainWindow.axaml`/`.axaml.cs`, `MainViewModel`, `TodoItemViewModel`).
  1·2차에서 입력 폼 전체가 항상 펼쳐져 있어 시각적으로 피로하고 창도 가로로 넓어 "리스트" 느낌이 안 난다는
  피드백으로 진행. 실행 확인 완료.
  - 창을 세로로 긴 비율(`Width=400, Height=700`)로 변경. 목록(`ListBox`)이 남은 공간을 채움.
  - "할 일 제목/마감일/중요·긴급/카테고리 지정/체크리스트/반복 설정" 입력 폼을 **"+ 할 일 추가" 버튼의
    Flyout**으로 옮김 — 평소엔 안 보이다가 버튼 눌렀을 때만 뜸. 그 안에서 체크리스트·반복 설정은 각각
    `Expander`로 기본 접어둠(자주 안 쓰는 항목이라 기본 노출 안 함).
  - 카테고리 관리(추가/이름·색상 수정/삭제)도 같은 방식으로 별도 **"카테고리 관리" 버튼의 Flyout**으로 이동
    (기존엔 화면에 항상 펼쳐진 칩 UI였음).
  - `DatePicker` 2곳(마감일, 반복 종료일)과 반복 간격 `NumericUpDown`에 각각 무엇을 지정하는 컨트롤인지
    라벨을 붙임 — 사용자가 처음 보고 "반복 종료일 위의 숫자가 뭘 의미하는지 바로 알기 어렵다"고 지적해서
    보완. 비슷한 모호함이 남아있는 컨트롤을 발견하면 그때그때 라벨을 추가하는 식으로 진행함.
  - 목록 항목(`ListBox.ItemTemplate`)을 가로 800px 기준 레이아웃(제목 고정폭 220px 등)에서 세로로 긴
    400px 창에 맞게 재구성 — 제목은 `Grid`의 `*` 컬럼으로 바꿔 줄바꿈되게 하고, 중요/긴급/반복/카테고리
    배지는 제목 줄 아래 별도 `WrapPanel`로 내려서 가로 오버플로 방지.
  - **버그 발견 및 수정**: `ITodoRepository`에 애초에 `Delete`가 없어서 반복 종료일 없는 항목을 영원히
    지울 수 없었음(위 "핵심 흐름" 섹션의 `DeleteTodo` 항목 참고). 목록 각 항목에 ✕ 삭제 버튼을 추가하고,
    `MemoNoteViewModel.ConfirmDeleteRequested`와 같은 패턴(`Func<Task<bool>>` 콜백을 View가 채워줌)으로
    삭제 전 확인창을 띄우게 함.
- [ ] 캐릭터 엔진 설계 (`ITodoEventBus` 구독 측 포함)

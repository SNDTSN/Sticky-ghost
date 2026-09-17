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
    RecurrenceDaysOfWeek INTEGER,       -- Weekly용 비트마스크 (월=1,화=2,수=4...)
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

// 백그라운드 타이머 (예: 1분마다)
CheckDueSoon():
    for item in repository.GetActiveWithDueDate():
        if item.DueDate - now <= threshold and not item.NotifiedDueSoon:
            item.NotifiedDueSoon = true
            eventBus.Publish(TodoDueSoon(item, minutesLeft))
        elif item.DueDate < now:
            eventBus.Publish(TodoOverdue(item))
```

## 다음 단계

- [x] 엔티티(`TodoItem`/`ChecklistItem`/`Category`/`RecurrenceRule`) + 리포지토리·이벤트버스 인터페이스 작성 (`src/App.Core/Domain/`)
- [ ] `TodoService` (AddTodo/CompleteTodo/CheckDueSoon 비즈니스 로직)
- [ ] `SqliteTodoRepository` 등 이 문서의 DDL 기준 실제 SQLite 구현
- [ ] 메모 위젯 설계
- [ ] 캐릭터 엔진 설계 (`ITodoEventBus` 구독 측 포함)

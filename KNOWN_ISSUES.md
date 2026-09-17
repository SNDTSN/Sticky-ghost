# Known Issues

지금 당장 고칠 만큼 급하진 않지만, 나중에 규모가 커지거나 다른 작업을 하다가 다시 마주칠 수 있어서
추적해두는 문제들을 모아둔 문서. 새로 발견한 게 있으면 여기 추가한다.

## 1. 효율성 — 사소한 변경마다 전체 목록을 통째로 재조회/재구성

**어디**: `App.UI/ViewModels/MainViewModel.cs`의 `LoadItems()` (`AddTodo`/`CompleteTodo`/`ToggleChecklistItem`
전부 이걸 다시 호출), `App.Core/Infrastructure/Sqlite/SqliteTodoRepository.cs`의 `GetIncomplete()` /
`GetActiveWithDueDate()`.

**증상**: 체크리스트 항목 하나만 체크해도 `LoadItems()`가 호출되어 `GetIncomplete()` 전체를 다시 쿼리하고,
모든 `TodoItemViewModel`/`ChecklistItemViewModel`을 새로 만든다. 게다가 `GetIncomplete()`/
`GetActiveWithDueDate()`는 목록을 가져온 뒤 **항목마다 별도 쿼리**로 체크리스트를 불러오는 N+1 패턴이다
(`SqliteTodoRepository.cs`의 `foreach (var item in items) item.ChecklistItems = LoadChecklistItems(...)`
부분). `TodoService.CheckDueSoon`도 마감 임박 알림을 보낼 항목마다 별도 커넥션으로 `Save`를 호출한다.

**왜 지금은 안 심각한가**: 개인용 위젯 앱이라 항목 수가 수십 개 수준일 걸로 예상되고, SQLite 로컬 파일이라
쿼리 자체도 빠르다. 체감되는 문제는 없다.

**개선 방향 (나중에 항목 수가 늘어나면)**:
- 변경된 항목 하나만 갱신하도록 `MainViewModel`을 손보기 (전체 `Items.Clear()` + 재구성 대신, 해당
  `TodoItemViewModel`만 교체/갱신).
- 체크리스트 로딩을 `TodoItemId IN (...)` 한 번의 쿼리로 모아서 가져오거나, `TodoItem`/`ChecklistItem`을
  JOIN해서 한 번에 읽기.

## 2. `SqliteSchemaInitializer` — 실제 스키마 마이그레이션 메커니즘이 없음

**어디**: `App.Core/Infrastructure/Sqlite/SqliteSchemaInitializer.cs`.

**증상**: `CREATE TABLE IF NOT EXISTS`로만 스키마를 만든다. 테이블이 이미 존재하면 그 안의 컬럼 구성은
전혀 안 건드린다. 이번에 `TodoItem.CompletionCount` 컬럼을 추가할 때, 마침 로컬에 기존 DB 파일이 없어서
문제가 안 됐지만, 만약 이미 그 스키마로 저장된 DB(사용자에게 배포된 이후 등)가 존재하는 상태에서 컬럼을
추가하면 `CREATE TABLE IF NOT EXISTS`는 아무 일도 안 하므로 **새 컬럼이 반영되지 않고, 그 컬럼을 참조하는
INSERT/SELECT 쿼리가 전부 런타임 오류로 깨진다.**

**개선 방향**: 실제 배포가 시작되기 전에, `PRAGMA user_version` 등으로 스키마 버전을 추적하고 필요한
`ALTER TABLE ... ADD COLUMN`을 순차 적용하는 간단한 마이그레이션 절차를 넣어야 한다. 지금 단계(문서 자체가
"아직 마이그레이션 도구 없이 처리한다"고 명시)에서는 급하지 않지만, 배포 이후 스키마 변경이 필요해지는
**첫 순간부터는 반드시** 해결하고 넘어가야 함.

## 3. 메모 위젯 — 새 메모 캐스케이드 위치 미구현

**어디**: `App.UI/Views/MainWindow.axaml.cs`의 `OnNewMemoClick`.

**증상**: `docs/memo-design.md`의 `CreateNewMemo` 의사코드는 "캐스케이드 위치"(새 메모마다 위치를 조금씩
어긋나게 배치)를 명시하고 있는데, 실제 구현은 항상 `PositionX = 100, PositionY = 100`으로 고정돼 있다.
"새 메모" 버튼을 여러 번 누르면 메모 창들이 전부 같은 자리에 완전히 겹쳐서 뜬다 (안 보이는 건 아니고,
드래그해서 옮기기 전까진 서로 가려져 있음).

**발견 경위**: 메모 위젯 생명주기(시작 시 복원, 메인 창 종료 시 함께 종료) 버그를 고치면서
`OnNewMemoClick`을 들여다보다가 발견. 이번 작업 범위 밖이라 손대지 않음.

**개선 방향**: `OpenMemoWindow` 호출 전에 마지막으로 생성된 메모 위치에 일정 오프셋(예: +24px, +24px)을
더해서 캐스케이드 배치하면 된다. 화면 경계를 벗어나면 원점으로 되돌리는 처리도 같이 고려.

## 4. `HexColorToBrushConverter` — 변환마다 새 Brush 할당 (사소함)

**어디**: `App.UI/Converters/HexColorToBrushConverter.cs`.

**증상**: 바인딩이 갱신될 때마다(카테고리 색상 변경, 메모 배경색 변경 등) `new SolidColorBrush(color)`를
매번 새로 생성한다. 같은 색상 문자열이어도 캐싱 없이 매번 새 객체를 만든다.

**왜 지금은 안 심각한가**: 색상 변경 빈도 자체가 낮고(사용자가 색상 선택기를 조작할 때만), 이 앱 규모에서
GC 부담이 체감될 수준이 아니다.

**개선 방향**: 필요해지면 `Dictionary<string, IBrush>` 정도로 간단히 캐싱 가능. 지금은 안 건드려도 됨.

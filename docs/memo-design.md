# 메모 위젯 설계

`docs/design-draft.md`의 "메모 위젯" 항목을 구체화한 문서. Windows의 (사라진) 스티커 메모와 비슷한, 바탕화면에 떠 있는 포스트잇 형태의 메모 창을 여러 개 띄울 수 있게 한다.

## 데이터 모델

```
Entity MemoNote
    Id: Guid
    Content: string
    Color: string (hex)          // 자유 색상 선택기로 지정
    PositionX: double            // 화면상 창 위치
    PositionY: double
    Width: double
    Height: double
    IsPinned: bool                // 항상 위 표시 여부, 메모별 토글 (기본 false)
    CreatedAt: DateTime
    UpdatedAt: DateTime
```

To-do의 `TodoService` 같은 별도 서비스 계층은 두지 않는다. 검증할 불변식이 없어서(필수 제목도 없음) ViewModel이 리포지토리를 직접 호출해도 충분하기 때문. 나중에 규칙이 생기면 그때 뽑아낸다.

```
interface IMemoRepository
    void Save(MemoNote memo)
    void Delete(Guid id)
    IEnumerable<MemoNote> GetAll()
```

## 확정한 UX 결정

* **닫기(X) 동작**: 확인 대화상자. 단, `Content`가 비어있으면(공백/개행만 있는 경우 포함 — 구현은 `string.IsNullOrWhiteSpace`) 확인 없이 바로 삭제한다 (빈 메모까지 확인창을 띄우면 번거로움). 대화상자는 Avalonia 자체 모달 창으로 충분 — 플랫폼 인터페이스 불필요.
* **항상 위 표시(always-on-top)**: 기본값은 꺼짐. 메모 창마다 개별 토글 버튼으로 켜고 끌 수 있음 (`IsPinned` 필드 자체가 이미 메모별 값이라 자연스럽게 지원됨).
* **색상**: 프리셋이 아닌 자유 색상 선택기.

## 핵심 흐름 (의사코드)

```
OnAppStartup:
    for memo in memoRepository.GetAll():
        OpenMemoWindow(memo)

CreateNewMemo():
    memo = new MemoNote(기본색, 캐스케이드 위치, 기본 크기, IsPinned=false)
    memoRepository.Save(memo)
    OpenMemoWindow(memo)

OpenMemoWindow(memo):
    window = new MemoWindow(memo에 바인딩된 ViewModel)
    windowBehavior.SetAlwaysOnTop(handle, memo.IsPinned)

    이동/리사이즈 끝날 때(디바운스) -> Position/Size 갱신 후 Save
    텍스트 변경 시(디바운스, ~500ms) -> Content, UpdatedAt 갱신 후 Save
    색상 선택기에서 변경 시 -> Color 갱신 후 Save
    Pin 토글 버튼 -> IsPinned 반전 -> windowBehavior.SetAlwaysOnTop 재호출 -> Save

    닫기(X) 클릭 시:
        if Content가 비어있으면:
            memoRepository.Delete(memo.Id) 후 창 닫기
        else:
            "이 메모를 삭제하시겠습니까?" 확인 대화상자
            확인 -> Delete 후 닫기 / 취소 -> 창 유지
```

## SQLite 스키마 (DDL)

```sql
CREATE TABLE MemoNote (
    Id          TEXT PRIMARY KEY,      -- GUID
    Content     TEXT NOT NULL DEFAULT '',
    Color       TEXT NOT NULL,          -- hex
    PositionX   REAL NOT NULL,
    PositionY   REAL NOT NULL,
    Width       REAL NOT NULL,
    Height      REAL NOT NULL,
    IsPinned    INTEGER NOT NULL DEFAULT 0,
    CreatedAt   TEXT NOT NULL,
    UpdatedAt   TEXT NOT NULL
);
```

동시에 열리는 메모 창 개수가 많아야 수십 개 수준이라 별도 인덱스는 두지 않는다 (`GetAll()`이 풀 스캔해도 충분히 빠름).

## 다음 단계

- [x] 데이터 모델 + UX 결정 + DDL 확정 (이 문서)
- [x] 엔티티/리포지토리 인터페이스 작성 (`src/App.Core/Domain/`)
- [x] `SqliteMemoRepository` 구현 + `SqliteSchemaInitializer`에 `MemoNote` 테이블 추가 (저장/조회/수정/삭제 스모크 테스트로 검증)
- [x] `App.UI`에 `MemoWindow` 뷰/뷰모델 작성 — 저장/삭제/핀 토글/색상 변경까지 실행해서 육안으로 확인 완료
  - 주의: `ColorPicker`는 `FluentTheme`에 자동 포함되지 않음. `App.axaml`에 `avares://Avalonia.Controls.ColorPicker/Themes/Fluent/Fluent.xaml` StyleInclude를 추가해야 렌더링됨
  - 이번 구현은 `App.Platform.Stub`으로 임시 배선 (플랫폼 구현체 선택/DI 조립은 캐릭터 엔진 설계 때 함께 정리 예정, `MainWindow.axaml.cs`에 TODO 표시해둠)
- [x] 메모 창 생명주기를 `OnAppStartup` 의사코드와 실제로 맞춤 (`MainWindow.axaml.cs`)
  - 시작 시 `_memoRepository.GetAll()`을 순회해 저장된 메모 창을 전부 다시 엶 (`OpenMemoWindow` 공통 헬퍼로 새 메모 생성 경로와 통합)
  - Avalonia 기본 `ShutdownMode`(`OnLastWindowClose`)로는 메모 창이 떠 있으면 메인 창을 닫아도 프로세스가 안 죽는 문제가 있어, `App.axaml.cs`에서 `ShutdownMode.OnMainWindowClose`로 명시 (메인 창 종료 = 앱 전체 종료)
  - 메인 창이 닫힐 때 열려 있는 모든 메모 창의 디바운스 중인 위치/크기/내용 저장을 즉시 실행(`MemoWindow.FlushPendingSave`)하도록 해서, 강제 종료로 마지막 편집분이 유실되지 않게 함
- [x] 버그 수정: `MemoWindow`가 OS 기본 타이틀바를 쓰고 있어서, 커스텀 ✕ 버튼(`RequestCloseCommand`)이 아니라 타이틀바 자체의 OS 닫기 버튼을 누르면 확인 대화상자/빈 메모 삭제 로직을 완전히 우회하는 문제 발견.
  `SystemDecorations="None"`으로 바꿔서 닫기 경로를 커스텀 ✕ 버튼 하나로 통일. 대신 OS가 대신 해주던 이동/리사이즈가 없어지므로
  헤더 영역 `PointerPressed` → `BeginMoveDrag`(이동), 우측 하단 리사이즈 그립 `PointerPressed` → `BeginResizeDrag(WindowEdge.SouthEast, ...)`(리사이즈)를
  `MemoWindow.axaml.cs`에 직접 구현 (`docs/design-draft.md`의 "메모리 누수 주의" 원칙과는 무관, 순수 UX 회귀 방지).
- [x] 창 위치 복원 보강 (2026-09-20) — 저장된 위치가 화면 밖으로 잘렸을 때(모니터 분리/해상도 변경) 헤더를 잡을 수 없게 되는 문제 대응
  - `ScreenPlacement.RestoreOrClamp`: 상단 모서리가 작업 영역 안에 있고 가로로 64px 이상 겹치면 그대로 복원, 아니면 가장 가까운
    작업 영역 안으로 clamp(크기 유지). "조금이라도 겹치면 통과"로 하면 헤더가 화면 위로 잘린 채 복구 불가능해져서 상단 모서리 기준으로 판정.
    보정된 위치는 `PositionChanged` → 기존 디바운스 저장 경로로 DB에도 반영됨. 메인/캐릭터 창과 같은 검증 규칙을 공유.
  - 새 메모 시작 위치: 이번 실행의 첫 메모는 메인 창 바로 옆(왼쪽 → 오른쪽 순으로 통째로 들어갈 자리가 있는 쪽)에서 시작하고,
    이후는 기존대로 +24px 캐스케이드. 캐스케이드가 작업 영역을 벗어나면 화면 좌측 원점 (100,100)에서 다시 시작(기존 동작 유지).

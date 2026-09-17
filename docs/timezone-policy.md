# 시간대 정책 (Timezone Policy)

## 왜 이 문서가 있는가

`docs/design-draft.md`에 명시된 대로 Sticky Ghost는 **한국인 사용자만을 대상**으로 한다. 그래서 이 프로젝트는 여러 시간대를 지원하려는 시도를 하지 않고, **모든 시각을 시스템 로컬 시간(=한국 사용자 기준 KST, UTC+9) 그대로 다룬다.** UTC로 저장했다가 표시 시점에 변환하는 방식은 쓰지 않는다.

> **Why this document exists**
> As stated in `docs/design-draft.md`, Sticky Ghost targets **Korean users only**. Because of that, the project makes no attempt to support multiple timezones — it treats **every timestamp as the system's local wall-clock time** (which, for the target users, is KST / UTC+9). There is no "store as UTC, convert on display" layer anywhere.

이 규칙이 한 번 깨졌던 적이 있다: `TodoItem.DueDate`(사용자가 `DatePicker`로 고른 로컬 시각)와 `TodoService.CheckDueSoon`이 비교 기준으로 쓰던 `IClock.UtcNow`(UTC)가 서로 다른 기준이라, "마감 임박" 판정이 실제보다 9시간(한국 시간대 오프셋만큼) 늦게 발생하는 잠복 버그가 있었다. 지금은 전부 로컬 시간으로 통일해서 고쳤지만, 앞으로 이 원칙이 다시 깨지지 않도록 로컬 시간에 의존하는 지점을 전부 아래에 목록화한다.

> This rule was broken once: `TodoItem.DueDate` (a local time the user picks via `DatePicker`) and the `IClock.UtcNow` (UTC) that `TodoService.CheckDueSoon` used as its comparison baseline were on two different bases, causing "due soon" detection to fire roughly 9 hours late (exactly Korea's UTC offset). It has since been fixed by unifying everything to local time. The list below exists so that rule doesn't get silently broken again.

## 현재 규칙

* **저장·비교·표시 전부 `DateTime.Now` 기준 로컬 시각.** `DateTime.UtcNow`는 이 코드베이스 어디에서도 쓰지 않는다.
* SQLite에는 로컬 시각이 타임존 정보 없이(naive) 문자열로 저장된다. 읽어올 때도 별도 변환 없이 그대로 로컬 시각으로 취급한다.
* `RecurrenceRule.ComputeNext` 등 날짜 연산 로직은 타임존을 전혀 모른다 — 호출자가 항상 로컬 시각을 넘겨준다는 전제로만 동작한다.

> **Current rule**
> * **Storage, comparison, and display all use local time via `DateTime.Now`.** `DateTime.UtcNow` is not used anywhere in this codebase.
> * SQLite stores local timestamps as timezone-naive strings. On read, they're treated as local time as-is, with no conversion.
> * Date arithmetic such as `RecurrenceRule.ComputeNext` is timezone-agnostic — it only works correctly because every caller always passes it a local-time value.

## 로컬 시간에 의존하는 지점 (전체 목록)

| 파일 | 위치 | 내용 |
|---|---|---|
| `src/App.Core/Domain/Services/IClock.cs` | `Now` 프로퍼티 | "현재 시각"의 유일한 추상화 지점. 여기서 로컬/UTC 여부가 결정된다 |
| `src/App.Core/Domain/Services/SystemClock.cs` | `Now => DateTime.Now;` | `IClock`의 실제 구현. 여기가 진짜 시스템 시각을 가져오는 곳 |
| `src/App.Core/Domain/Services/TodoService.cs` | `AddTodo`의 `CreatedAt = _clock.Now` | 할 일 생성 시각 |
| " | `CompleteTodo`의 `var now = _clock.Now` | 완료 처리 시각(`CompletedAt`, `CompletionLog`) 계산 기준 |
| " | `CheckDueSoon`의 `var now = _clock.Now` | `DueDate`와 비교해 마감 임박/초과를 판정하는 기준 — **과거 버그가 났던 지점** |
| `src/App.Core/Domain/Entities/TodoItem.cs` | `CreatedAt` 필드 기본값 | `TodoService`를 거치지 않고 `TodoItem`을 직접 생성할 경우의 기본값 |
| `src/App.Core/Domain/Entities/MemoNote.cs` | `CreatedAt`/`UpdatedAt` 필드 기본값 | 메모 생성 시각 기본값 |
| `src/App.UI/ViewModels/MemoNoteViewModel.cs` | 콘텐츠 저장 디바운스 타이머 안 `_memo.UpdatedAt = DateTime.Now` | 메모 내용 수정 시각 |
| `src/App.UI/ViewModels/MainViewModel.cs` | `NewDueDate?.DateTime`, `NewRecurrenceEndDate?.DateTime` | `DatePicker`가 주는 `DateTimeOffset`에서 오프셋을 버리고 로컬 벽시계 값만 꺼내는 지점. `.UtcDateTime`을 쓰면 다시 예전 버그(하루가 밀리는 문제)가 재발하니 절대 바꾸면 안 됨 |

> **Every place that depends on local time (full list)**
> See the table above — file, exact location, and what it's used for. The `TodoService.CheckDueSoon` row is where the original bug lived. The `MainViewModel` row is a landmine: it must keep using `.DateTime` (not `.UtcDateTime`) on the `DatePicker`'s `DateTimeOffset`, or the old "date shifts by one day" bug comes back.

## 나중에 다중 시간대를 지원하고 싶다면

이 앱을 다른 나라 사용자용으로 고치고 싶다면, "로컬 시각 하나로 통일" 전제 자체를 깨야 하므로 아래 방향을 권장한다.

1. **저장은 UTC, 표시만 사용자 시간대로 변환**하는 통상적인 방식으로 전환한다. 즉 `IClock`을 다시 UTC 기준으로 되돌리고(`Now` → `UtcNow`), 사용자의 시간대(`TimeZoneInfo`)를 설정값으로 추가한다.
2. 위 목록의 모든 지점에서 "로컬 시각을 그대로 저장/비교"하던 부분을 "UTC로 저장 → 화면에 뿌릴 때만 사용자 시간대로 변환"하는 구조로 바꾼다. 특히 `TodoService.CheckDueSoon`처럼 두 시각을 직접 빼는 연산은 반드시 같은 기준(둘 다 UTC)으로 맞춰야 한다 — 이번에 고친 버그가 정확히 이 원칙이 깨져서 난 것이다.
3. `MainViewModel`에서 `DatePicker`로부터 받는 `DateTimeOffset`을 사용자 시간대 기준으로 UTC로 변환해서 저장하도록 바꾼다.
4. `RecurrenceRule.ComputeNext`류의 순수 날짜 연산 로직 자체는 안 건드려도 된다 — 타임존을 모르는 게 정상이며, 호출하는 쪽에서 일관된 기준(전부 UTC 또는 전부 특정 로컬)만 지켜주면 된다.
5. 기존 SQLite 데이터는 전부 "로컬 시각(naive)"으로 저장되어 있으므로, 하나의 스키마 안에 UTC 데이터와 로컬 데이터가 섞이지 않도록 마이그레이션이 필요하다.

> **If you want to support multiple timezones later**
> 1. Switch to the conventional approach: **store UTC, convert to the user's timezone only at display time.** Revert `IClock` to a UTC-based `Now`/`UtcNow`, and add the user's `TimeZoneInfo` as a setting.
> 2. In every location listed in the table above, change "store/compare local time directly" to "store UTC, convert only when rendering." In particular, anywhere that subtracts one timestamp from another (like `TodoService.CheckDueSoon`) must have both sides on the same basis (both UTC) — that exact principle being broken is what caused the bug this document fixes.
> 3. In `MainViewModel`, convert the `DateTimeOffset` coming from `DatePicker` to UTC using the user's timezone before storing it.
> 4. Pure date-arithmetic logic like `RecurrenceRule.ComputeNext` doesn't need to change — it's fine for it to stay timezone-agnostic, as long as every caller is consistent about what basis (all-UTC, or all-some-fixed-local) it feeds in.
> 5. Existing SQLite data is stored as naive local time, so a migration is needed to avoid mixing UTC and local values within the same column.

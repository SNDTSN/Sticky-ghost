using App.Core.Domain.Entities;

namespace App.Core.Domain.Services;

/// <summary>
/// 미완료 할 일 목록의 정렬 기준. 마감일(날짜) → 아이젠하워 사분면 → 생성순 → Id 순으로 비교한다.
/// 규칙 자체는 화면 표시가 아니라 도메인 정책이라 App.Core에 둔다(docs/todo-design.md "목록 정렬 규칙").
///
/// Id까지 비교하므로 서로 다른 항목의 키는 절대 같지 않다(= 전순서). 덕분에 목록에 항목 하나를 끼워 넣을
/// 위치가 유일하게 정해지고, 그렇게 증분 갱신한 순서가 전체를 다시 정렬한 결과와 항상 일치한다 —
/// "재시작하면 순서가 달라진다"는 문제가 생길 수 없다.
/// </summary>
public readonly record struct TodoSortKey(DateTime DueDay, int Quadrant, DateTime CreatedAt, Guid Id)
    : IComparable<TodoSortKey>
{
    public static TodoSortKey From(TodoItem item) => new(
        // 마감일은 "날짜"까지만 본다 — 같은 날 항목들을 일부러 동률로 만들어 사분면이 그날 안의 순서를 정하게 한다.
        // 마감 시각은 DueDateRule.FromDateOnly가 그날 23:59:59로 맞추므로 날짜만 고른 항목끼리는 시각도 같지만,
        // 나중에 시각 입력이 생겨도 사분면 정렬이 조용히 무력화되지 않게 하려고 처음부터 날짜 단위로 자른다.
        // 마감일이 없는 항목은 맨 뒤로 보낸다.
        item.DueDate?.Date ?? DateTime.MaxValue,
        QuadrantOf(item),
        item.CreatedAt,
        item.Id);

    /// <summary>
    /// 아이젠하워 사분면을 작을수록 위로 오는 순위로 바꾼다 — 0 중요+긴급 / 1 긴급 / 2 중요 / 3 없음.
    /// "긴급 우선"은 사용자 결정(2026-09-20): 같은 날 안에서는 "지금 뭐부터?"에 바로 답하는 순서를 택했다.
    /// 중요 우선(0 중요+긴급 / 1 중요 / 2 긴급 / 3 없음)으로 바꾸려면 아래 두 항의 가중치를 맞바꾸면 된다.
    /// </summary>
    private static int QuadrantOf(TodoItem item) => (item.IsUrgent ? 0 : 2) + (item.IsImportant ? 0 : 1);

    public int CompareTo(TodoSortKey other)
    {
        var byDueDay = DueDay.CompareTo(other.DueDay);
        if (byDueDay != 0)
            return byDueDay;

        var byQuadrant = Quadrant.CompareTo(other.Quadrant);
        if (byQuadrant != 0)
            return byQuadrant;

        var byCreatedAt = CreatedAt.CompareTo(other.CreatedAt);
        if (byCreatedAt != 0)
            return byCreatedAt;

        // 같은 틱에 만들어진 항목까지 순서를 확정해 전순서를 완성한다(값 자체에 의미는 없다).
        return Id.CompareTo(other.Id);
    }

    public static bool operator <(TodoSortKey left, TodoSortKey right) => left.CompareTo(right) < 0;

    public static bool operator >(TodoSortKey left, TodoSortKey right) => left.CompareTo(right) > 0;

    public static bool operator <=(TodoSortKey left, TodoSortKey right) => left.CompareTo(right) <= 0;

    public static bool operator >=(TodoSortKey left, TodoSortKey right) => left.CompareTo(right) >= 0;
}

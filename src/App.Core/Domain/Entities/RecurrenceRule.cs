namespace App.Core.Domain.Entities;

public sealed class RecurrenceRule
{
    public RecurrenceType Type { get; }
    public int Interval { get; }
    public IReadOnlySet<DayOfWeek>? DaysOfWeek { get; }
    public DateTime? EndDate { get; }

    public RecurrenceRule(RecurrenceType type, int interval, IReadOnlySet<DayOfWeek>? daysOfWeek = null, DateTime? endDate = null)
    {
        if (interval < 1)
            throw new ArgumentOutOfRangeException(nameof(interval), "Interval은 1 이상이어야 합니다.");

        // MVP 제한: 여러 요일(DaysOfWeek) 조합은 "매주"(Interval=1)에서만 지원.
        // "격주로 월/수/금" 같은 조합은 anchor 개념이 필요해 다음 단계로 미룸 (docs/todo-design.md 논의 참고).
        if (daysOfWeek is { Count: > 0 } && interval != 1)
            throw new ArgumentException("DaysOfWeek가 설정된 경우 Interval은 1이어야 합니다.", nameof(interval));

        Type = type;
        Interval = interval;
        DaysOfWeek = daysOfWeek;
        EndDate = endDate;
    }

    /// <summary>
    /// 현재 마감일 기준 다음 회차의 마감일을 계산한다.
    /// 호출자는 currentDueDate가 실제 값(non-null)임을 보장해야 한다.
    /// </summary>
    public DateTime ComputeNext(DateTime currentDueDate) => Type switch
    {
        RecurrenceType.Daily => currentDueDate.AddDays(Interval),
        RecurrenceType.Monthly => currentDueDate.AddMonths(Interval),
        RecurrenceType.Weekly => ComputeNextWeekly(currentDueDate),
        _ => throw new NotSupportedException($"지원하지 않는 RecurrenceType: {Type}"),
    };

    private DateTime ComputeNextWeekly(DateTime currentDueDate)
    {
        if (DaysOfWeek is not { Count: > 0 })
            return currentDueDate.AddDays(7 * Interval);

        for (var offset = 1; offset <= 7; offset++)
        {
            var candidate = currentDueDate.AddDays(offset);
            if (DaysOfWeek.Contains(candidate.DayOfWeek))
                return candidate;
        }

        // DaysOfWeek가 비어있지 않은 한 7일 이내에 반드시 걸리므로 도달 불가.
        throw new InvalidOperationException("DaysOfWeek 내에서 다음 날짜를 찾지 못했습니다.");
    }
}

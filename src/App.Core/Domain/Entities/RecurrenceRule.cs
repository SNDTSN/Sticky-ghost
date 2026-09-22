using App.Core.Diagnostics;

namespace App.Core.Domain.Entities;

public sealed class RecurrenceRule
{
    /// <summary>
    /// <see cref="ComputeNextAfter"/>가 한 번에 굴릴 수 있는 회차 수 상한. 어떤 규칙이든 최소 1일씩 전진하므로
    /// 정상적인 입력으로는 도달할 수 없다(매일 반복을 27년 밀려야 닿는다). 규칙이 전진하지 못하는 버그가
    /// 생겼을 때 UI 스레드를 영원히 붙잡지 않게 하려는 안전망이다.
    /// </summary>
    private const int MaxRollovers = 10_000;

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

        // 요일 지정은 Weekly 전용이라는 기존 규칙을 말일 모드에도 적용한다("매달 말일이면서 월/수/금"은 성립하지 않는다).
        if (type == RecurrenceType.MonthlyLastDay && daysOfWeek is { Count: > 0 })
            throw new ArgumentException("MonthlyLastDay는 DaysOfWeek와 함께 쓸 수 없습니다.", nameof(daysOfWeek));

        Type = type;
        Interval = interval;
        DaysOfWeek = daysOfWeek;
        EndDate = endDate;
    }

    /// <summary>
    /// 현재 마감일 기준 다음 회차의 마감일을 계산한다(딱 한 회차만 굴린다).
    /// 호출자는 currentDueDate가 실제 값(non-null)임을 보장해야 한다.
    /// 지연 완료까지 고려해 "지금 이후의 회차"를 찾을 때는 <see cref="ComputeNextAfter"/>를 쓴다.
    /// </summary>
    public DateTime ComputeNext(DateTime currentDueDate) => Type switch
    {
        RecurrenceType.Daily => currentDueDate.AddDays(Interval),
        RecurrenceType.Monthly => currentDueDate.AddMonths(Interval),
        RecurrenceType.MonthlyLastDay => ComputeNextMonthEnd(currentDueDate),
        RecurrenceType.Weekly => ComputeNextWeekly(currentDueDate),
        _ => throw new NotSupportedException($"지원하지 않는 RecurrenceType: {Type}"),
    };

    /// <summary>
    /// 완료 시각(now)을 넘어서는 첫 회차의 마감일. 반복이 끝났으면 null(호출부는 그 회차를 마지막으로 완료 상태를 유지한다).
    ///
    /// 옛 마감일 기준으로 딱 한 번만 굴리면 매일 반복을 5일 밀렸다가 완료했을 때 다음 마감이 "어제"가 된다.
    /// 따라잡으려고 다섯 번 체크하게 만들면 CompletionLog와 CompletionCount가 "실제로 한 일"과 어긋나므로,
    /// 건너뛴 회차는 기록하지 않고 마감일만 앞으로 굴린다(KNOWN_ISSUES #19-(2) b안, 2026-09-22 결정).
    ///
    /// 마감이 그날 23:59:59(<see cref="Services.DueDateRule"/>)이므로 "오늘 마감"은 오늘 안에 완료하면 아직 미래다.
    /// 즉 5일 밀린 매일 반복을 오늘 완료하면 다음 마감은 내일이 아니라 **오늘**이 된다(2026-09-22 결정).
    /// </summary>
    public DateTime? ComputeNextAfter(DateTime currentDueDate, DateTime now)
    {
        var next = ComputeNext(currentDueDate);

        for (var guard = 0; guard < MaxRollovers; guard++)
        {
            if (IsPastEnd(next))
                return null;

            if (next > now)
                return next;

            next = ComputeNext(next);
        }

        AppLog.Write("todo", $"반복 롤포워드 상한({MaxRollovers}) 도달: {Type}/{Interval}, due={currentDueDate:o}, now={now:o}");
        return next;
    }

    /// <summary>
    /// 종료일을 지났는지. **날짜 단위로** 비교하는 것이 중요하다 — 마감은 그날 23:59:59인데 종료일은 00:00으로
    /// 저장된 행(2026-09-22 이전에 만든 항목)이 섞여 있어서, 시각까지 비교하면 종료일 당일 회차가 하루 먼저 잘린다.
    /// </summary>
    private bool IsPastEnd(DateTime candidate) => EndDate is { } end && candidate.Date > end.Date;

    private DateTime ComputeNextMonthEnd(DateTime currentDueDate)
    {
        // AddMonths는 말일 초과(2/30 등)를 그 달의 마지막 날로 클램프하므로 d.Day <= lastDay가 항상 성립한다.
        var d = currentDueDate.AddMonths(Interval);
        var lastDay = DateTime.DaysInMonth(d.Year, d.Month);

        // new DateTime(...)이 아니라 AddDays로 옮긴다 — 마감 시각(23:59:59)과 DateTimeKind를 잃지 않으려고.
        return d.AddDays(lastDay - d.Day);
    }

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

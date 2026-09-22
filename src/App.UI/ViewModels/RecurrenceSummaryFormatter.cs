using System;
using System.Linq;
using App.Core.Domain.Entities;

namespace App.UI.ViewModels;

/// <summary>
/// 반복 규칙을 사람이 읽는 한 줄로 만든다("🔁 3일마다", "🔁 매주 화,목").
///
/// 목록의 항목 표시(<see cref="TodoItemViewModel"/>)와 할 일 추가 화면의 미리보기가 **같은 함수**를 쓴다 —
/// 두 벌로 갈라지면 입력창에서 만든 문구와 목록에 뜨는 문구가 달라지고, 한쪽만 고치는 사고가 난다.
/// </summary>
public static class RecurrenceSummaryFormatter
{
    // 요일 표시 순서 (docs/todo-design.md: 월=1,화=2,수=4... 와 동일한 순서)
    private static readonly (DayOfWeek Day, string Label)[] DayOrder =
    {
        (DayOfWeek.Monday, "월"), (DayOfWeek.Tuesday, "화"), (DayOfWeek.Wednesday, "수"),
        (DayOfWeek.Thursday, "목"), (DayOfWeek.Friday, "금"), (DayOfWeek.Saturday, "토"), (DayOfWeek.Sunday, "일"),
    };

    public static string? Format(RecurrenceRule? recurrence)
    {
        if (recurrence is null)
            return null;

        var unit = recurrence.Type switch
        {
            RecurrenceType.Daily => "일",
            RecurrenceType.Weekly => "주",
            RecurrenceType.Monthly => "달",
            RecurrenceType.MonthlyLastDay => "달",
            _ => "?",
        };

        string body;
        if (recurrence.Type == RecurrenceType.Weekly && recurrence.DaysOfWeek is { Count: > 0 } days)
        {
            var labels = DayOrder.Where(d => days.Contains(d.Day)).Select(d => d.Label);
            body = $"매주 {string.Join(",", labels)}";
        }
        else if (recurrence.Type == RecurrenceType.MonthlyLastDay)
        {
            body = recurrence.Interval == 1 ? "매달 말일" : $"{recurrence.Interval}달마다 말일";
        }
        else
        {
            body = recurrence.Interval == 1 ? $"매{unit}" : $"{recurrence.Interval}{unit}마다";
        }

        return recurrence.EndDate is { } end
            ? $"🔁 {body} (종료 {end:yyyy-MM-dd})"
            : $"🔁 {body}";
    }
}

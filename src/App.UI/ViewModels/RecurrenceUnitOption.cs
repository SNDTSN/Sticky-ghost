namespace App.UI.ViewModels;

/// <summary>
/// 할 일 추가 화면에서 고르는 반복 단위. 도메인의 <see cref="App.Core.Domain.Entities.RecurrenceType"/>와
/// 일부러 다른 타입이다 — <see cref="Weekday"/>("매주 월·수" 같은 요일 지정)는 도메인에 따로 있는 값이 아니라
/// <c>Weekly + Interval 1 + DaysOfWeek</c>의 조합이기 때문이다.
///
/// 화면에서 단위를 분리한 이유: 요일을 지정하면 도메인이 Interval을 무시하므로(RecurrenceRule.ComputeNextWeekly),
/// "2주마다 + 월·수" 같은 조합은 애초에 만들 수 없다. 단위로 갈라두면 모순된 입력이 화면에서 아예 나오지 않는다.
/// </summary>
public enum RecurrenceUnit
{
    Day,
    Week,
    Month,
    Weekday,
}

public sealed record RecurrenceUnitOption(RecurrenceUnit Unit, string Label);

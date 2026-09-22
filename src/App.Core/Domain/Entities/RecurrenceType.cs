namespace App.Core.Domain.Entities;

public enum RecurrenceType
{
    Daily,
    Weekly,
    Monthly,

    /// <summary>
    /// 매(N)달의 마지막 날. Monthly와 갈라 둔 이유: Monthly는 직전 회차의 마감일에 AddMonths를 하므로
    /// 1/31 → 2/28 → 3/28...로 한 번 내려간 날짜가 복귀하지 않는다(KNOWN_ISSUES #19-(1)).
    /// "매달 말일"이라는 의도는 회차마다 말일을 다시 계산해야 지켜지고, 그 의도는 코드가 추측할 수 없어
    /// 입력에서 직접 고르게 했다(2026-09-22 결정).
    /// DB에는 RecurrenceType이 TEXT로 저장되므로 이 값을 더하는 데 스키마 변경이 필요 없다.
    /// </summary>
    MonthlyLastDay,
}

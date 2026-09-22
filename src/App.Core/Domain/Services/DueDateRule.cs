namespace App.Core.Domain.Services;

/// <summary>
/// 날짜만 고른 마감을 언제로 볼 것인가에 대한 규칙.
///
/// 입력이 DatePicker뿐이라 마감이 항상 그날 00:00이었고, 그러면 "오늘까지 할 일"이 하루 종일 마감 초과로
/// 판정된다(KNOWN_ISSUES #19-(3)). 날짜만 있는 마감은 "그날 끝까지"로 해석한다(2026-09-22 결정).
///
/// 나중에 시각 입력(TimePicker)이 생기면 그 경로는 이 함수를 거치지 않고 사용자가 고른 시각을 그대로 쓴다 —
/// 그래서 정규화를 도메인(TodoService) 안쪽이 아니라 **입력 경로**에 둔다. 도메인에서 00:00을 보고 추측하면
/// 정말로 자정이 마감인 항목을 조용히 밀어버리게 된다.
///
/// 이미 저장된 00:00 마감 행은 손대지 않는다(표시·정렬에 영향이 없고, SQLite TEXT 포맷에 의존하는
/// 일괄 UPDATE가 더 위험하다 — 2026-09-22 결정).
/// </summary>
public static class DueDateRule
{
    private static readonly TimeSpan EndOfDay = new(23, 59, 59);

    /// <summary>날짜만 고른 마감/종료일을 그날 23:59:59로 만든다. 시각 성분이 있어도 버리고 그날 끝으로 맞춘다.</summary>
    public static DateTime FromDateOnly(DateTime date) => date.Date + EndOfDay;
}

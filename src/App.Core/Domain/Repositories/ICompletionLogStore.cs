namespace App.Core.Domain.Repositories;

public interface ICompletionLogStore
{
    /// <summary>완료 당시 체크리스트 상태(JSON 스냅샷)를 이력으로 남긴다</summary>
    void Append(Guid todoItemId, DateTime completedOn, string? checklistSnapshot);
}

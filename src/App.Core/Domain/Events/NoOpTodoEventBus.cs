namespace App.Core.Domain.Events;

/// <summary>
/// 아무 것도 하지 않는 ITodoEventBus 구현체. 캐릭터 엔진 등 구독자가 생기기 전까지
/// TodoService를 조립할 때 임시로 사용한다 (App.Platform.Stub과 같은 성격).
/// </summary>
public sealed class NoOpTodoEventBus : ITodoEventBus
{
    public void Publish(TodoEvent @event) { }
}

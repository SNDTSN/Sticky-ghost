using App.Core.Diagnostics;

namespace App.Core.Domain.Events;

/// <summary>
/// 할 일 이벤트를 구독자들에게 그대로 전달하는 최소 구현. 구독자가 생기기 전까지 쓰는 NoOpTodoEventBus의 자리를 대신한다.
/// 발행 스레드에서 동기로 호출하므로, UI를 건드리는 구독자는 스스로 디스패처로 넘겨야 한다
/// (지금은 CompleteTodo가 UI 스레드에서 불리지만 CheckDueSoon은 백그라운드에서 올 수 있다).
/// </summary>
public sealed class TodoEventBus : ITodoEventBus
{
    private readonly List<Action<TodoEvent>> _handlers = new();

    public void Subscribe(Action<TodoEvent> handler) => _handlers.Add(handler);

    public void Publish(TodoEvent @event)
    {
        foreach (var handler in _handlers)
        {
            // Publish는 TodoService.CompleteTodo의 마지막 줄이다. 구독자가 던지면 완료 경로가 실패한 것처럼 보이고
            // 호출부의 목록 갱신까지 막히므로, 구독자의 사고는 여기서 끊고 기록만 남긴다.
            try
            {
                handler(@event);
            }
            catch (Exception ex)
            {
                AppLog.Write("todo", ex);
            }
        }
    }
}

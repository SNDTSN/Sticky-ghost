namespace App.Core.Domain.Events;

public interface ITodoEventBus
{
    void Publish(TodoEvent @event);
}

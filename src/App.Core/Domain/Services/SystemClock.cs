namespace App.Core.Domain.Services;

public sealed class SystemClock : IClock
{
    public DateTime Now => DateTime.Now;
}

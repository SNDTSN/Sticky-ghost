namespace App.Core.Domain.Services;

public interface IClock
{
    DateTime UtcNow { get; }
}

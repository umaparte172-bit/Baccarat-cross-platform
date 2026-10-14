namespace Baccarat.Core.Authentication;

public interface IAuthenticationClock
{
    DateTimeOffset GetUtcNow();
}

public sealed class SystemAuthenticationClock : IAuthenticationClock
{
    public static SystemAuthenticationClock Instance { get; } = new();

    private SystemAuthenticationClock()
    {
    }

    public DateTimeOffset GetUtcNow() => DateTimeOffset.UtcNow;
}

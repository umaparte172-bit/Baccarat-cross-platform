using System.Collections.Concurrent;
using Baccarat.Core.Security;

namespace Baccarat.Infrastructure.Security;

public sealed class VolatileSecretStore : ISecretStore
{
    private readonly ConcurrentDictionary<string, string> values = new(StringComparer.Ordinal);

    public ValueTask SaveAsync(string key, string value, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(value);
        values[key] = value;
        return ValueTask.CompletedTask;
    }

    public ValueTask<string?> ReadAsync(string key, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        values.TryGetValue(key, out var value);
        return ValueTask.FromResult(value);
    }

    public ValueTask DeleteAsync(string key, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        values.TryRemove(key, out _);
        return ValueTask.CompletedTask;
    }
}

namespace Baccarat.Core.Security;

public interface ISecretStore
{
    ValueTask SaveAsync(string key, string value, CancellationToken cancellationToken = default);
    ValueTask<string?> ReadAsync(string key, CancellationToken cancellationToken = default);
    ValueTask DeleteAsync(string key, CancellationToken cancellationToken = default);
}

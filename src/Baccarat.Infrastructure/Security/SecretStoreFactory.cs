using Baccarat.Core.Security;

namespace Baccarat.Infrastructure.Security;

public static class SecretStoreFactory
{
    public const string ServiceName = "com.baccarat.crossplatform";

    public static ISecretStore CreateDefault() => CreateForService(ServiceName);

    public static ISecretStore CreateForService(string serviceName)
    {
        if (string.IsNullOrWhiteSpace(serviceName))
            throw new ArgumentException("A service name is required.", nameof(serviceName));
        if (OperatingSystem.IsWindows())
            return new WindowsCredentialManagerSecretStore(serviceName);
        if (OperatingSystem.IsMacOS())
            return new MacOsKeychainSecretStore(serviceName);
        return new VolatileSecretStore();
    }
}

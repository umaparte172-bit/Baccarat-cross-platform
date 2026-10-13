using Baccarat.Core.Security;

namespace Baccarat.Infrastructure.Security;

public static class SecretStoreFactory
{
    public const string ServiceName = "com.baccarat.crossplatform";

    public static ISecretStore CreateDefault()
    {
        if (OperatingSystem.IsWindows())
            return new WindowsCredentialManagerSecretStore(ServiceName);
        if (OperatingSystem.IsMacOS())
            return new MacOsKeychainSecretStore(ServiceName);
        return new VolatileSecretStore();
    }
}

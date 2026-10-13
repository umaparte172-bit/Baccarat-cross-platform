using Baccarat.Core.Security;

namespace Baccarat.Infrastructure.Security;

public static class SecretStoreFactory
{
    public const string MacOsServiceName = "com.baccarat.crossplatform";

    public static ISecretStore CreateDefault() => OperatingSystem.IsMacOS()
        ? new MacOsKeychainSecretStore(MacOsServiceName)
        : new VolatileSecretStore();
}

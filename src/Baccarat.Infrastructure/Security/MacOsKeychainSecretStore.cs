using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using Baccarat.Core.Security;

namespace Baccarat.Infrastructure.Security;

/// <summary>Stores application secrets as generic-password items in the current user's macOS Keychain.</summary>
[SupportedOSPlatform("macos")]
public sealed class MacOsKeychainSecretStore : ISecretStore
{
    private const int MaximumKeyLength = 256;
    private const int MaximumValueBytes = 1024 * 1024;
    private readonly string service;
    private readonly SemaphoreSlim gate = new(1, 1);

    public MacOsKeychainSecretStore(string service)
    {
        if (!OperatingSystem.IsMacOS())
            throw new PlatformNotSupportedException("macOS Keychain storage is available only on macOS.");

        if (string.IsNullOrWhiteSpace(service))
            throw new ArgumentException("The service name is required.", nameof(service));
        this.service = service;
    }

    public async ValueTask SaveAsync(string key, string value, CancellationToken cancellationToken = default)
    {
        ValidateKey(key);
        ArgumentNullException.ThrowIfNull(value);
        var bytes = Encoding.UTF8.GetBytes(value);
        if (bytes.Length > MaximumValueBytes)
            throw new ArgumentException("The secret is too large for this store.", nameof(value));

        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            using var query = KeychainQuery.Create(service, key);
            using var update = KeychainQuery.CreateValue(bytes);
            var status = NativeMethods.SecItemUpdate(query.Handle, update.Handle);
            if (status == NativeMethods.ErrorItemNotFound)
            {
                query.SetValue(bytes);
                status = NativeMethods.SecItemAdd(query.Handle, out var ignored);
                if (ignored != IntPtr.Zero)
                    NativeMethods.CFRelease(ignored);
            }

            ThrowIfError(status, "save");
        }
        finally
        {
            CryptographicOperations.ZeroMemory(bytes);
            gate.Release();
        }
    }

    public async ValueTask<string?> ReadAsync(string key, CancellationToken cancellationToken = default)
    {
        ValidateKey(key);
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            using var query = KeychainQuery.Create(service, key);
            query.RequestData();
            var status = NativeMethods.SecItemCopyMatching(query.Handle, out var result);
            if (status == NativeMethods.ErrorItemNotFound)
                return null;

            ThrowIfError(status, "read");
            if (result == IntPtr.Zero)
                throw new CryptographicException("Keychain returned no data for a successful read.");

            try
            {
                var length = checked((int)NativeMethods.CFDataGetLength(result));
                if (length < 0 || length > MaximumValueBytes)
                    throw new CryptographicException("Keychain returned an invalid secret length.");

                var bytes = new byte[length];
                try
                {
                    if (length > 0)
                        Marshal.Copy(NativeMethods.CFDataGetBytePtr(result), bytes, 0, length);
                    return new UTF8Encoding(false, true).GetString(bytes);
                }
                finally
                {
                    CryptographicOperations.ZeroMemory(bytes);
                }
            }
            finally
            {
                NativeMethods.CFRelease(result);
            }
        }
        finally
        {
            gate.Release();
        }
    }

    public async ValueTask DeleteAsync(string key, CancellationToken cancellationToken = default)
    {
        ValidateKey(key);
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            using var query = KeychainQuery.Create(service, key);
            var status = NativeMethods.SecItemDelete(query.Handle);
            if (status != NativeMethods.ErrorItemNotFound)
                ThrowIfError(status, "delete");
        }
        finally
        {
            gate.Release();
        }
    }

    private static void ValidateKey(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
            throw new ArgumentException("The secret key is required.", nameof(key));
        if (key.Length > MaximumKeyLength)
            throw new ArgumentException("The secret key is too long.", nameof(key));
    }

    private static void ThrowIfError(int status, string operation)
    {
        if (status != NativeMethods.Success)
            throw new CryptographicException($"Unable to {operation} the Keychain item (OSStatus {status}).");
    }

    private sealed class KeychainQuery : IDisposable
    {
        private readonly List<IntPtr> ownedValues = [];

        private KeychainQuery()
        {
            Handle = NativeMethods.CFDictionaryCreateMutable(
                IntPtr.Zero,
                0,
                NativeMethods.DictionaryKeyCallbacks,
                NativeMethods.DictionaryValueCallbacks);
            if (Handle == IntPtr.Zero)
                throw new CryptographicException("Unable to allocate a Keychain query.");
        }

        public IntPtr Handle { get; }

        public static KeychainQuery Create(string service, string account)
        {
            var query = new KeychainQuery();
            query.Add(NativeMethods.SecClass, NativeMethods.SecClassGenericPassword);
            query.AddString(NativeMethods.SecAttrService, service);
            query.AddString(NativeMethods.SecAttrAccount, account);
            return query;
        }

        public static KeychainQuery CreateValue(byte[] value)
        {
            var query = new KeychainQuery();
            query.SetValue(value);
            return query;
        }

        public void RequestData()
        {
            Add(NativeMethods.SecReturnData, NativeMethods.BooleanTrue);
            Add(NativeMethods.SecMatchLimit, NativeMethods.SecMatchLimitOne);
        }

        public void SetValue(byte[] value)
        {
            var data = NativeMethods.CFDataCreate(IntPtr.Zero, value, value.Length);
            if (data == IntPtr.Zero)
                throw new CryptographicException("Unable to allocate Keychain value data.");
            ownedValues.Add(data);
            Add(NativeMethods.SecValueData, data);
        }

        private void AddString(IntPtr key, string value)
        {
            var nativeValue = NativeMethods.CFStringCreateWithCString(
                IntPtr.Zero,
                value,
                NativeMethods.Utf8Encoding);
            if (nativeValue == IntPtr.Zero)
                throw new CryptographicException("Unable to allocate a Keychain string.");
            ownedValues.Add(nativeValue);
            Add(key, nativeValue);
        }

        private void Add(IntPtr key, IntPtr value) =>
            NativeMethods.CFDictionarySetValue(Handle, key, value);

        public void Dispose()
        {
            foreach (var value in ownedValues)
                NativeMethods.CFRelease(value);
            NativeMethods.CFRelease(Handle);
        }
    }

    private static class NativeMethods
    {
        private const string CoreFoundation = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";
        private const string Security = "/System/Library/Frameworks/Security.framework/Security";

        public const int Success = 0;
        public const int ErrorItemNotFound = -25300;
        public const uint Utf8Encoding = 0x08000100;

        private static readonly IntPtr coreFoundationHandle = NativeLibrary.Load(CoreFoundation);
        private static readonly IntPtr securityHandle = NativeLibrary.Load(Security);

        public static IntPtr DictionaryKeyCallbacks { get; } = GetExport(coreFoundationHandle, "kCFTypeDictionaryKeyCallBacks");
        public static IntPtr DictionaryValueCallbacks { get; } = GetExport(coreFoundationHandle, "kCFTypeDictionaryValueCallBacks");
        public static IntPtr BooleanTrue { get; } = ReadExport(coreFoundationHandle, "kCFBooleanTrue");
        public static IntPtr SecClass { get; } = ReadExport(securityHandle, "kSecClass");
        public static IntPtr SecClassGenericPassword { get; } = ReadExport(securityHandle, "kSecClassGenericPassword");
        public static IntPtr SecAttrService { get; } = ReadExport(securityHandle, "kSecAttrService");
        public static IntPtr SecAttrAccount { get; } = ReadExport(securityHandle, "kSecAttrAccount");
        public static IntPtr SecValueData { get; } = ReadExport(securityHandle, "kSecValueData");
        public static IntPtr SecReturnData { get; } = ReadExport(securityHandle, "kSecReturnData");
        public static IntPtr SecMatchLimit { get; } = ReadExport(securityHandle, "kSecMatchLimit");
        public static IntPtr SecMatchLimitOne { get; } = ReadExport(securityHandle, "kSecMatchLimitOne");

        [DllImport(CoreFoundation)]
        public static extern IntPtr CFDictionaryCreateMutable(
            IntPtr allocator,
            nint capacity,
            IntPtr keyCallbacks,
            IntPtr valueCallbacks);

        [DllImport(CoreFoundation)]
        public static extern void CFDictionarySetValue(IntPtr dictionary, IntPtr key, IntPtr value);

        [DllImport(CoreFoundation)]
        public static extern IntPtr CFStringCreateWithCString(
            IntPtr allocator,
            [MarshalAs(UnmanagedType.LPUTF8Str)] string value,
            uint encoding);

        [DllImport(CoreFoundation)]
        public static extern IntPtr CFDataCreate(IntPtr allocator, byte[] bytes, nint length);

        [DllImport(CoreFoundation)]
        public static extern nint CFDataGetLength(IntPtr data);

        [DllImport(CoreFoundation)]
        public static extern IntPtr CFDataGetBytePtr(IntPtr data);

        [DllImport(CoreFoundation)]
        public static extern void CFRelease(IntPtr value);

        [DllImport(Security)]
        public static extern int SecItemAdd(IntPtr attributes, out IntPtr result);

        [DllImport(Security)]
        public static extern int SecItemCopyMatching(IntPtr query, out IntPtr result);

        [DllImport(Security)]
        public static extern int SecItemUpdate(IntPtr query, IntPtr attributesToUpdate);

        [DllImport(Security)]
        public static extern int SecItemDelete(IntPtr query);

        private static IntPtr GetExport(IntPtr library, string name) => NativeLibrary.GetExport(library, name);

        private static IntPtr ReadExport(IntPtr library, string name) => Marshal.ReadIntPtr(GetExport(library, name));
    }
}

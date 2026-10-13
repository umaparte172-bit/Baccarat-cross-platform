using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using Baccarat.Core.Security;

namespace Baccarat.Infrastructure.Security;

/// <summary>Stores application secrets as generic credentials for the current Windows user.</summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsCredentialManagerSecretStore : ISecretStore
{
    private const int MaximumKeyLength = 256;
    private const int MaximumValueBytes = 2560;
    private readonly string service;
    private readonly SemaphoreSlim gate = new(1, 1);

    public WindowsCredentialManagerSecretStore(string service)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("Windows Credential Manager storage is available only on Windows.");

        ArgumentException.ThrowIfNullOrWhiteSpace(service);
        this.service = service;
    }

    public async ValueTask SaveAsync(string key, string value, CancellationToken cancellationToken = default)
    {
        var target = GetTarget(key);
        ArgumentNullException.ThrowIfNull(value);
        var bytes = Encoding.UTF8.GetBytes(value);
        if (bytes.Length > MaximumValueBytes)
            throw new ArgumentException("The secret is too large for Windows Credential Manager.", nameof(value));

        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        var targetPointer = IntPtr.Zero;
        var userPointer = IntPtr.Zero;
        var valuePointer = IntPtr.Zero;
        try
        {
            targetPointer = Marshal.StringToCoTaskMemUni(target);
            userPointer = Marshal.StringToCoTaskMemUni(service);
            valuePointer = Marshal.AllocCoTaskMem(bytes.Length);
            if (bytes.Length > 0)
                Marshal.Copy(bytes, 0, valuePointer, bytes.Length);

            var credential = new NativeCredential
            {
                Type = NativeMethods.CredentialTypeGeneric,
                TargetName = targetPointer,
                CredentialBlobSize = checked((uint)bytes.Length),
                CredentialBlob = valuePointer,
                Persist = NativeMethods.CredentialPersistLocalMachine,
                UserName = userPointer
            };
            if (!NativeMethods.CredWrite(ref credential, 0))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Unable to save the credential.");
        }
        finally
        {
            CryptographicOperations.ZeroMemory(bytes);
            if (valuePointer != IntPtr.Zero)
            {
                Marshal.Copy(new byte[bytes.Length], 0, valuePointer, bytes.Length);
                Marshal.FreeCoTaskMem(valuePointer);
            }
            if (userPointer != IntPtr.Zero)
                Marshal.FreeCoTaskMem(userPointer);
            if (targetPointer != IntPtr.Zero)
                Marshal.FreeCoTaskMem(targetPointer);
            gate.Release();
        }
    }

    public async ValueTask<string?> ReadAsync(string key, CancellationToken cancellationToken = default)
    {
        var target = GetTarget(key);
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!NativeMethods.CredRead(target, NativeMethods.CredentialTypeGeneric, 0, out var pointer))
            {
                var error = Marshal.GetLastWin32Error();
                if (error == NativeMethods.ErrorNotFound)
                    return null;
                throw new Win32Exception(error, "Unable to read the credential.");
            }

            try
            {
                var credential = Marshal.PtrToStructure<NativeCredential>(pointer);
                if (credential.CredentialBlobSize > MaximumValueBytes)
                    throw new CryptographicException("Credential Manager returned an invalid secret length.");

                var bytes = new byte[credential.CredentialBlobSize];
                try
                {
                    if (bytes.Length > 0)
                        Marshal.Copy(credential.CredentialBlob, bytes, 0, bytes.Length);
                    return new UTF8Encoding(false, true).GetString(bytes);
                }
                finally
                {
                    CryptographicOperations.ZeroMemory(bytes);
                }
            }
            finally
            {
                NativeMethods.CredFree(pointer);
            }
        }
        finally
        {
            gate.Release();
        }
    }

    public async ValueTask DeleteAsync(string key, CancellationToken cancellationToken = default)
    {
        var target = GetTarget(key);
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!NativeMethods.CredDelete(target, NativeMethods.CredentialTypeGeneric, 0))
            {
                var error = Marshal.GetLastWin32Error();
                if (error != NativeMethods.ErrorNotFound)
                    throw new Win32Exception(error, "Unable to delete the credential.");
            }
        }
        finally
        {
            gate.Release();
        }
    }

    private string GetTarget(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        if (key.Length > MaximumKeyLength)
            throw new ArgumentException("The secret key is too long.", nameof(key));
        return $"{service}:{key}";
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeCredential
    {
        public uint Flags;
        public uint Type;
        public IntPtr TargetName;
        public IntPtr Comment;
        public long LastWritten;
        public uint CredentialBlobSize;
        public IntPtr CredentialBlob;
        public uint Persist;
        public uint AttributeCount;
        public IntPtr Attributes;
        public IntPtr TargetAlias;
        public IntPtr UserName;
    }

    private static class NativeMethods
    {
        public const uint CredentialTypeGeneric = 1;
        public const uint CredentialPersistLocalMachine = 2;
        public const int ErrorNotFound = 1168;

        [DllImport("Advapi32.dll", EntryPoint = "CredWriteW", SetLastError = true, ExactSpelling = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool CredWrite(ref NativeCredential credential, uint flags);

        [DllImport("Advapi32.dll", EntryPoint = "CredReadW", SetLastError = true, CharSet = CharSet.Unicode,
            ExactSpelling = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool CredRead(string target, uint type, uint flags, out IntPtr credential);

        [DllImport("Advapi32.dll", EntryPoint = "CredDeleteW", SetLastError = true, CharSet = CharSet.Unicode,
            ExactSpelling = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool CredDelete(string target, uint type, uint flags);

        [DllImport("Advapi32.dll", ExactSpelling = true)]
        public static extern void CredFree(IntPtr buffer);
    }
}

using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using ErkS.Platform.Core;

namespace ErkS.Studio;

internal sealed class WindowsAuthenticodeTrustVerifier : IAuthenticodeTrustVerifier
{
    private static readonly Guid GenericVerifyV2 = new("00AAC56B-CD44-11D0-8CC2-00C04FC295EE");

    public AuthenticodeTrustResult Verify(string path)
    {
        if (!OperatingSystem.IsWindows())
            return new AuthenticodeTrustResult(false, "", "Authenticode verification requires Windows.");

        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var fileInfo = new WinTrustFileInfo(path);
        IntPtr fileInfoPointer = Marshal.AllocHGlobal(Marshal.SizeOf<WinTrustFileInfo>());
        Marshal.StructureToPtr(fileInfo, fileInfoPointer, fDeleteOld: false);
        var trustData = new WinTrustData(fileInfoPointer);
        Guid policy = GenericVerifyV2;

        try
        {
            SignerIdentity signer = ReadSigner(path);
            int status = WinVerifyTrust(IntPtr.Zero, ref policy, trustData);
            if (status != 0)
            {
                string code = $"0x{unchecked((uint)status):X8}";
                string detail = Marshal.GetExceptionForHR(status)?.Message
                    ?? new Win32Exception(status).Message;
                return new AuthenticodeTrustResult(
                    false,
                    signer.Publisher,
                    $"Authenticode verification failed ({code}): {detail}",
                    signer.CertificateSha256,
                    unchecked((uint)status));
            }

            if (string.IsNullOrWhiteSpace(signer.Publisher))
            {
                return new AuthenticodeTrustResult(
                    false,
                    "",
                    "The trusted Authenticode signature does not contain a publisher name.");
            }

            return new AuthenticodeTrustResult(
                true,
                signer.Publisher,
                "",
                signer.CertificateSha256);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            return new AuthenticodeTrustResult(
                false,
                "",
                "Authenticode verification failed: " + exception.Message);
        }
        finally
        {
            trustData.StateAction = WinTrustDataStateAction.Close;
            _ = WinVerifyTrust(IntPtr.Zero, ref policy, trustData);
            Marshal.DestroyStructure<WinTrustFileInfo>(fileInfoPointer);
            Marshal.FreeHGlobal(fileInfoPointer);
        }
    }

    private static SignerIdentity ReadSigner(string path)
    {
        try
        {
#pragma warning disable SYSLIB0057
            using X509Certificate certificate = X509Certificate.CreateFromSignedFile(path);
#pragma warning restore SYSLIB0057
            using X509Certificate2 signer = X509CertificateLoader.LoadCertificate(certificate.GetRawCertData());
            string publisher = signer.GetNameInfo(X509NameType.SimpleName, forIssuer: false);
            if (string.IsNullOrWhiteSpace(publisher))
                publisher = signer.SubjectName.Name ?? "";
            string certificateSha256 = Convert.ToHexString(SHA256.HashData(signer.RawData));
            return new SignerIdentity(publisher, certificateSha256);
        }
        catch (CryptographicException)
        {
            return new SignerIdentity("", "");
        }
    }

    private sealed record SignerIdentity(string Publisher, string CertificateSha256);

    [DllImport("wintrust.dll", ExactSpelling = true, SetLastError = false)]
    private static extern int WinVerifyTrust(
        IntPtr windowHandle,
        [In] ref Guid actionId,
        [In, Out] WinTrustData trustData);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private sealed class WinTrustFileInfo
    {
        public uint StructSize = (uint)Marshal.SizeOf<WinTrustFileInfo>();

        [MarshalAs(UnmanagedType.LPWStr)]
        public string FilePath;

        public IntPtr FileHandle = IntPtr.Zero;
        public IntPtr KnownSubject = IntPtr.Zero;

        public WinTrustFileInfo(string filePath) => FilePath = filePath;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private sealed class WinTrustData
    {
        private const uint UiNone = 2;
        private const uint RevokeWholeChain = 1;
        private const uint ChoiceFile = 1;
        private const uint RevocationCheckChainExcludeRoot = 0x00000080;
        private const uint DisableMd2Md4 = 0x00002000;

        public uint StructSize = (uint)Marshal.SizeOf<WinTrustData>();
        public IntPtr PolicyCallbackData = IntPtr.Zero;
        public IntPtr SipClientData = IntPtr.Zero;
        public uint UiChoice = UiNone;
        public uint RevocationChecks = RevokeWholeChain;
        public uint UnionChoice = ChoiceFile;
        public IntPtr FileInfo;
        public WinTrustDataStateAction StateAction = WinTrustDataStateAction.Verify;
        public IntPtr StateData = IntPtr.Zero;
        public IntPtr UrlReference = IntPtr.Zero;
        public uint ProviderFlags = RevocationCheckChainExcludeRoot | DisableMd2Md4;
        public uint UiContext = 0;

        public WinTrustData(IntPtr fileInfo) => FileInfo = fileInfo;
    }

    private enum WinTrustDataStateAction : uint
    {
        Verify = 1,
        Close = 2,
    }
}

using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Security.Cryptography;

namespace AutoTest.FlashLab.Programming;

/// <summary>
/// The one user-approved SafetyFile, shipped with AutoTest rather than selected
/// or edited by an ECU test project. Extracted only after digest verification.
/// Never executes this DLL: the audited CanTool provider selects its supported
/// implementation based on the already-verified original binary identity.
/// </summary>
public static class FixedSafetyFile
{
    public const string ExpectedSha256 = CanToolSecurityProvider.KnownReferenceSafetyFileSha256;
    private const string EmbeddedName = "AutoTest.FlashLab.FixedSafetyFileGzip";
    private static readonly object Sync = new();

    public static string EnsureAvailable()
    {
        lock (Sync)
        {
            using var resource = typeof(FixedSafetyFile).Assembly.GetManifestResourceStream(EmbeddedName)
                ?? throw new InvalidDataException("AutoTest 安装缺少固定安全算法资源，请重新安装软件。");
            using var decompressor = new GZipStream(resource, CompressionMode.Decompress);
            using var data = new MemoryStream();
            decompressor.CopyTo(data);
            var bytes = data.ToArray();
            if (bytes.Length != 4096 ||
                !string.Equals(Convert.ToHexString(SHA256.HashData(bytes)),
                    ExpectedSha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("固定安全算法资源校验失败，禁止烧录。");

            var directory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "AutoTest", "fixed-security");
            Directory.CreateDirectory(directory);
            var path = Path.Combine(directory, "SafetyFile.dll");
            if (!File.Exists(path))
            {
                var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
                try
                {
                    File.WriteAllBytes(temporary, bytes);
                    File.Move(temporary, path, overwrite: false);
                }
                finally { if (File.Exists(temporary)) File.Delete(temporary); }
            }
            // Fail closed if a local file was replaced or altered. A valid
            // embedded copy alone is insufficient when the runtime path is bad.
            using var saved = File.OpenRead(path);
            var hash = Convert.ToHexString(SHA256.HashData(saved));
            if (!string.Equals(hash, ExpectedSha256, StringComparison.OrdinalIgnoreCase))
                throw new IOException("AutoTest 固定安全算法文件发生变化，禁止烧录。请重新安装软件。");
            return path;
        }
    }
}

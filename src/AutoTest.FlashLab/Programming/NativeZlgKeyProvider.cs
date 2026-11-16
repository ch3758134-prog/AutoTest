using System.Buffers.Binary;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;

namespace AutoTest.FlashLab.Programming;

public sealed record KeyDllInspection(string Path,string Sha256,bool NativeX64,
    bool KnownDemonstration,string Detail);

/// <summary>Owns a native CanTool-compatible Cdecl ZLGKey library for one authorized
/// programming job. NO algorithm fallback. Never log seed, key or exported key bytes.</summary>
public sealed class NativeZlgKeyProvider : IDisposable
{
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int ZlgKey(byte[] seed,ushort seedSize,uint securityLevel,
        IntPtr variant,[Out]byte[] output,ref ushort outputLength);
    private readonly IntPtr _library;
    private readonly ZlgKey _compute;
    private bool _closed;

    public static KeyDllInspection Inspect(string path)
    {
        var full=Path.GetFullPath(path);
        if(!File.Exists(full))throw new FileNotFoundException("Seed/Key DLL 不存在。",full);
        var bytes=File.ReadAllBytes(full);
        string hash=Convert.ToHexString(SHA256.HashData(bytes));
        if(bytes.Length<512 || bytes[0]!='M' || bytes[1]!='Z')
            throw new FormatException("Seed/Key 文件不是有效的 PE DLL。");
        int offset=BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(0x3C,4));
        if(offset<64 || offset>bytes.Length-256 ||
            !bytes.AsSpan(offset,4).SequenceEqual(new byte[]{(byte)'P',(byte)'E',0,0}))
            throw new FormatException("Seed/Key PE 文件头无效。");
        ushort machine=BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(offset+4,2));
        int opt=offset+24;
        ushort magic=BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(opt,2));
        bool x64=machine==0x8664 && magic==0x20B;
        bool managed=false;
        if(x64)
        {
            int clrDirectory=opt+112+14*8;
            if(clrDirectory+4<=bytes.Length)
                managed=BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(clrDirectory,4))!=0;
        }
        bool demo=hash.Equals("A8D22A0F82D6F68910FBBF2D81AFAD35D19C804953AD33C80F5143DA8C98A703",
            StringComparison.OrdinalIgnoreCase);
        return new(full,hash,x64 && !managed,demo,
            x64 && !managed ? (demo?"原生 x64 演示 DLL，非 ECU 算法匹配证明":"原生 x64 候选，导出需运行时检查")
                : "非原生 x64 ZLGKey 目标；不能使用 CanTool 原始原生函数 ABI");
    }

    public NativeZlgKeyProvider(KeyDllInspection inspection)
    {
        if(!OperatingSystem.IsWindows() || !Environment.Is64BitProcess || !inspection.NativeX64)
            throw new PlatformNotSupportedException("ZLGKey 原生 Provider 只能运行在 Windows x64。");
        string current=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(inspection.Path)));
        if(!string.Equals(current,inspection.Sha256,StringComparison.OrdinalIgnoreCase))
            throw new IOException("DLL 文件在预检后改变，拒绝加载。");
        _library=NativeLibrary.Load(inspection.Path);
        try
        {
            if(!NativeLibrary.TryGetExport(_library,"ZLGKey",out var fn))
                throw new EntryPointNotFoundException("DLL 不含原生 ZLGKey；托管 SafetyFile.dll 不能直接使用。");
            _compute=Marshal.GetDelegateForFunctionPointer<ZlgKey>(fn);
        }
        catch { NativeLibrary.Free(_library);throw; }
    }
    public byte[] Calculate(byte[] seed)
    {
        if(_closed)throw new ObjectDisposedException(nameof(NativeZlgKeyProvider));
        if(seed.Length is < 4 or > 128)throw new FormatException("ECU Seed 长度异常。");
        // CanTool source uses level=0, variant=null; no hidden fallback.
        byte[] output=new byte[64];
        ushort length=(ushort)output.Length;
        int status=_compute(seed,checked((ushort)seed.Length),0,IntPtr.Zero,output,ref length);
        if(status!=0 || length is < 1 or > 64)
            throw new InvalidOperationException($"原生 ZLGKey 失败：return={status}，输出长度={length}。");
        byte[] result=output.AsSpan(0,length).ToArray();
        CryptographicOperations.ZeroMemory(output);
        return result;
    }
    public void Dispose()
    {
        if(_closed)return;_closed=true;
        NativeLibrary.Free(_library);
    }
}

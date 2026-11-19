using System.IO;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Runtime.Loader;
using System.Security.Cryptography;

namespace AutoTest.FlashLab.Programming;

/// <summary>CanTool-compatible provider selection. A managed SafetyFile and a native
/// ZLGKey are TWO different ABIs; never silently substitute one for the other.</summary>
public enum CanToolSecurityKind { ManagedSafetyFile, NativeZlgKey, CanToolReferenceBuiltInLfsr }

public sealed record CanToolSecurityInspection(
    string Path,string Sha256,CanToolSecurityKind Kind,bool KnownDemo,string Api)
{
    public string Summary=>Kind switch
    {
        CanToolSecurityKind.ManagedSafetyFile=>
            "托管安全库 SafetyVerification.SafetyVerificater.SafetyVerificate(byte[]) → byte[]",
        CanToolSecurityKind.CanToolReferenceBuiltInLfsr=>
            "CanTool 参考内置 LFSR（原生加载不兼容后的源码备用算法；ECU 兼容性未验证）",
        _=>"原生 x64 安全库 ZLGKey(seed, size, level, variant, key, keyLen)"
    };
}

public interface ICanToolSecurityProvider : IDisposable
{
    byte[] Calculate(byte[] seed);
}

public static class CanToolSecurityProvider
{
    // Exact SHA of the user's original 4096-byte CLR SafetyFile.dll.
    // Original CanTool's LoadLibrary/GetProcAddress("ZLGKey") cannot call it.
    // Issue #25 established that original source's explicit fallback matches
    // the separately supplied sa_demo.dll static disassembly. Only this exact
    // known binary permits the deterministic reference-internal provider.
    public const string KnownReferenceSafetyFileSha256=
        "EE2C4A2791B88DCE19DB29F308752C8F79BC0654463D43E6880F65C6FDEA3833";
    private const string ManagedType="SafetyVerification.SafetyVerificater";
    private const string ManagedMethod="SafetyVerificate";
    // Signature observed in supplied, unexecuted SafetyFile.dll:
    // calling-convention DEFAULT/static, 1 argument, byte[] return, byte[] argument.
    private static readonly byte[] ManagedSignature=[0x00,0x01,0x1D,0x05,0x1D,0x05];

    public static CanToolSecurityInspection Inspect(string path)
    {
        var full=Path.GetFullPath(path);
        if(!File.Exists(full))throw new FileNotFoundException("安全算法 DLL 不存在。",full);
        var info=new FileInfo(full);
        if(info.Length is < 512 or > 16*1024*1024)
            throw new FormatException("安全算法 DLL 大小不在允许范围。");
        string hash;
        using(var file=File.OpenRead(full))
            hash=Convert.ToHexString(SHA256.HashData(file));
        using(var file=File.OpenRead(full))
        using(var pe=new PEReader(file))
        {
            if(pe.PEHeaders.CorHeader is not null)
            {
                if(!pe.HasMetadata)throw new FormatException("CLR DLL 缺少有效的 .NET 元数据。");
                var md=pe.GetMetadataReader();
                foreach(var handle in md.TypeDefinitions)
                {
                    var type=md.GetTypeDefinition(handle);
                    var fullName=md.GetString(type.Namespace)+"."+md.GetString(type.Name);
                    if(!string.Equals(fullName,ManagedType,StringComparison.Ordinal))continue;
                    foreach(var methodHandle in type.GetMethods())
                    {
                        var method=md.GetMethodDefinition(methodHandle);
                        if(md.GetString(method.Name)!=ManagedMethod ||
                           !method.Attributes.HasFlag(MethodAttributes.Static) ||
                           !method.Attributes.HasFlag(MethodAttributes.Public))continue;
                        var signature=md.GetBlobBytes(method.Signature);
                        if(signature.AsSpan().SequenceEqual(ManagedSignature))
                        {
                            // The original native CanTool entry point would
                            // not load this CLR-only DLL, so the archived
                            // reference takes its internal LFSR path.
                            bool exactReference=hash.Equals(KnownReferenceSafetyFileSha256,
                                StringComparison.OrdinalIgnoreCase);
                            return new(full,hash,
                                exactReference?CanToolSecurityKind.CanToolReferenceBuiltInLfsr:
                                    CanToolSecurityKind.ManagedSafetyFile,
                                false,exactReference?
                                    "CanTool internal 35-round LFSR (reference-source fallback)":
                                    ManagedType+"."+ManagedMethod+"(byte[]) -> byte[]");
                        }
                    }
                }
                throw new NotSupportedException(
                    "此托管 DLL 未提供实际 SafetyFile.dll 的公开静态 byte[] SafetyVerificate(byte[]) 接口。");
            }
        }
        // Native path retains the original CanTool Cdecl ZLGKey ABI.
        var native=NativeZlgKeyProvider.Inspect(full);
        if(!native.NativeX64)
            throw new NotSupportedException("原生 ZLGKey 仅支持 x64；不允许错误架构 DLL。");
        return new(full,hash,CanToolSecurityKind.NativeZlgKey,native.KnownDemonstration,
            "Cdecl ZLGKey(byte[],ushort,uint,char*,byte[],ushort*)");
    }

    public static ICanToolSecurityProvider Open(CanToolSecurityInspection inspection)
    {
        if(!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("安全算法执行仅支持 Windows。");
        using(var file=File.OpenRead(inspection.Path))
        {
            var hash=Convert.ToHexString(SHA256.HashData(file));
            if(!string.Equals(hash,inspection.Sha256,StringComparison.OrdinalIgnoreCase))
                throw new IOException("算法库自冻结任务后已更改。");
        }
        return inspection.Kind switch
        {
            CanToolSecurityKind.ManagedSafetyFile=>new ManagedSafetyFileProvider(inspection),
            CanToolSecurityKind.CanToolReferenceBuiltInLfsr=>
                new ReferenceLfsrAdapter(inspection),
            CanToolSecurityKind.NativeZlgKey=>
                new NativeAdapter(new NativeZlgKeyProvider(
                    NativeZlgKeyProvider.Inspect(inspection.Path))),
            _=>throw new NotSupportedException("未知的安全算法库类型。")
        };
    }

    private sealed class ReferenceLfsrAdapter(CanToolSecurityInspection inspection)
        : ICanToolSecurityProvider
    {
        public byte[] Calculate(byte[] seed)
        {
            if(inspection.Kind!=CanToolSecurityKind.CanToolReferenceBuiltInLfsr)
                throw new InvalidOperationException("参考算法身份不匹配。");
            return CanToolReferenceLfsr.Calculate(seed);
        }
        public void Dispose(){}
    }

    private sealed class NativeAdapter(NativeZlgKeyProvider native) : ICanToolSecurityProvider
    {
        public byte[] Calculate(byte[] seed)=>native.Calculate(seed);
        public void Dispose()=>native.Dispose();
    }

    private sealed class ManagedSafetyFileProvider : ICanToolSecurityProvider
    {
        private readonly AssemblyLoadContext _context;
        private MethodInfo? _method;
        public ManagedSafetyFileProvider(CanToolSecurityInspection inspection)
        {
            _context=new AssemblyLoadContext("CanToolSafetyFile-"+Guid.NewGuid().ToString("N"),
                isCollectible:true);
            try
            {
                var assembly=_context.LoadFromAssemblyPath(inspection.Path);
                var type=assembly.GetType(ManagedType,throwOnError:true)!;
                _method=type.GetMethod(ManagedMethod,BindingFlags.Public|BindingFlags.Static,
                    binder:null,types:[typeof(byte[])],modifiers:null)
                    ?? throw new MissingMethodException(ManagedType,ManagedMethod);
                if(_method.ReturnType!=typeof(byte[]))
                    throw new MissingMethodException("SafetyFile.dll 的返回类型不是 byte[]。");
            }
            catch { _context.Unload();throw; }
        }
        public byte[] Calculate(byte[] seed)
        {
            if(_method is null)throw new ObjectDisposedException(nameof(ManagedSafetyFileProvider));
            // The supplied SafetyFile.dll IL reads seed[0] through seed[3].
            if(seed.Length is < 4 or > 128)throw new FormatException("SafetyFile Seed 必须至少4字节。");
            // No logging of seed, key or intermediate values; no fallback provider.
            try
            {
                var key=_method.Invoke(null,[seed]) as byte[];
                if(key is null || key.Length is < 1 or > 64)
                    throw new InvalidDataException("SafetyFile.dll 未返回有效长度的 Key。");
                return key;
            }
            catch(TargetInvocationException ex)
            {
                throw new InvalidOperationException("SafetyFile.dll 执行失败，停止安全访问。",
                    ex.InnerException ?? ex);
            }
        }
        public void Dispose()
        {
            _method=null;
            _context.Unload();
        }
    }
}


/// <summary>
/// Exact 35-step algorithm recovered from the provided native sa_demo.dll's
/// ZLGKey RVA 0x1430 disassembly; Issue #25 traces the same implementation
/// to CanTool's SOURCE fallback. This does NOT prove the target ECU uses it.
/// Never use with unknown safety files, and never silently switch on error.
/// </summary>
public static class CanToolReferenceLfsr
{
    public static byte[] Calculate(byte[] seed)
    {
        if(seed is null || seed.Length!=4)
            throw new FormatException("CanTool 参考内置算法只支持 4 字节 Seed。");
        uint state=((uint)seed[0]<<24)|((uint)seed[1]<<16)|
            ((uint)seed[2]<<8)|seed[3];
        for(int i=0;i<35;i++)
            state=(state<<1)^((state&0x80000000U)!=0?0xC2DFF1C2U:0U);
        return [(byte)(state>>24),(byte)(state>>16),
            (byte)(state>>8),(byte)state];
    }
}

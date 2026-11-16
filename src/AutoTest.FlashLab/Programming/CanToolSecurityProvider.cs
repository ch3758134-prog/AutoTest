using System.IO;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Runtime.Loader;
using System.Security.Cryptography;

namespace AutoTest.FlashLab.Programming;

/// <summary>CanTool-compatible provider selection. A managed SafetyFile and a native
/// ZLGKey are TWO different ABIs; never silently substitute one for the other.</summary>
public enum CanToolSecurityKind { ManagedSafetyFile, NativeZlgKey }

public sealed record CanToolSecurityInspection(
    string Path,string Sha256,CanToolSecurityKind Kind,bool KnownDemo,string Api)
{
    public string Summary=>Kind==CanToolSecurityKind.ManagedSafetyFile
        ? "托管安全库 SafetyVerification.SafetyVerificater.SafetyVerificate(byte[]) → byte[]"
        : "原生 x64 安全库 ZLGKey(seed, size, level, variant, key, keyLen)";
}

public interface ICanToolSecurityProvider : IDisposable
{
    byte[] Calculate(byte[] seed);
}

public static class CanToolSecurityProvider
{
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
                            return new(full,hash,CanToolSecurityKind.ManagedSafetyFile,false,
                                ManagedType+"."+ManagedMethod+"(byte[]) -> byte[]");
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
            CanToolSecurityKind.NativeZlgKey=>
                new NativeAdapter(new NativeZlgKeyProvider(
                    NativeZlgKeyProvider.Inspect(inspection.Path))),
            _=>throw new NotSupportedException("未知的安全算法库类型。")
        };
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

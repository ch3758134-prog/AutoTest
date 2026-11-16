using System.IO.Compression;
using System.Text;
using System.Text.Json;
using AutoTest.FlashLab.Firmware;
using AutoTest.FlashLab.Configuration;

namespace AutoTest.FlashLab.Evidence;

/// <summary>Human-invoked F1 export. Raw trace is local; no credential or DLL execution.
/// Contains potentially identifying ECU DID data: operator controls distribution.</summary>
public static class FlashLabEvidenceExporter
{
    public static void Export(string zipPath,string? rawJsonl,CanToolXmlProfileReader configuration,
        FirmwareSelection? image,IEnumerable<string> visibleLog)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(zipPath))!);
        using var output=new FileStream(zipPath,FileMode.Create,FileAccess.Write,FileShare.None);
        using var zip=new ZipArchive(output,ZipArchiveMode.Create,leaveOpen:false);
        var summary=new
        {
            Product="AutoTest FlashLab F1",ExportedUtc=DateTimeOffset.UtcNow,
            Scope="OFFLINE_CONFIG_IMAGE_OR_EXPLICIT_READ_DID_ONLY",
            Forbidden="F2 flash, security, erase, download, reset; no program success claim",
            ConfigurationSource=configuration.SourcePath,
            Fields=configuration.Parameters.Select(p=>new {p.Name,p.Value,p.Source}).ToArray(),
            UnknownXmlElements=configuration.UnmappedNames,
            Image=image,
            Important="USB ACK does not prove CAN1 reception; hardware-exclusive access and HOST regression require Windows verification."
        };
        Write(zip,"session.json",JsonSerializer.Serialize(summary,
            new JsonSerializerOptions {WriteIndented=true}));
        Write(zip,"ui-log.txt",string.Join(Environment.NewLine,visibleLog));
        if(!string.IsNullOrWhiteSpace(rawJsonl) && File.Exists(rawJsonl))
        {
            // Live trace writer uses FileShare.Read and AutoFlush; read-only access permitted.
            var item=zip.CreateEntry("device-raw.jsonl",CompressionLevel.Optimal);
            using var dest=item.Open();
            using var src=new FileStream(rawJsonl,FileMode.Open,FileAccess.Read,
                FileShare.ReadWrite);
            src.CopyTo(dest);
        }
    }
    private static void Write(ZipArchive zip,string name,string body)
    {
        var entry=zip.CreateEntry(name,CompressionLevel.Optimal);
        using var stream=new StreamWriter(entry.Open(),new UTF8Encoding(false));
        stream.Write(body);
    }
}

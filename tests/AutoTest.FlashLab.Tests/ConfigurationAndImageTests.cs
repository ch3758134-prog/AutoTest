using System.IO;
using System.Xml;
using System.Xml.Linq;
using AutoTest.FlashLab.Configuration;
using AutoTest.FlashLab.Firmware;
using Xunit;

namespace AutoTest.FlashLab.Tests;

/// <summary>Deterministic file/validation tests only. No ECU, CAN mock or real hardware.</summary>
public sealed class ConfigurationAndImageTests
{
    [Fact]
    public void CanToolXml_LoadEditSaveReload_PreservesUnmappedFields()
    {
        string src=Path.Combine(Path.GetTempPath(),Guid.NewGuid().ToString("N")+".xml");
        string dst=Path.Combine(Path.GetTempPath(),Guid.NewGuid().ToString("N")+".xml");
        try
        {
            File.WriteAllText(src,"<Configuration><APPPhyID>0x747</APPPhyID>"+
                "<APPRespond>0x74F</APPRespond><BootFeature>73</BootFeature>"+
                "<FlashStartAddr>0x10008000</FlashStartAddr>"+
                "<FlashEndAddr>0x1006FFFF</FlashEndAddr></Configuration>");
            var cfg=CanToolXmlProfileReader.Load(src);
            Assert.Equal((0x747u,0x74Fu),cfg.GetEndpoint(false));
            Assert.Contains("BootFeature",cfg.UnmappedNames);
            cfg.Set("APPPhyID","0x748");
            cfg.ValidateConfiguredRange();
            cfg.SaveAs(dst);
            Assert.Equal("73",XDocument.Load(dst).Descendants("BootFeature").Single().Value);
            var reopen=CanToolXmlProfileReader.Load(dst);
            Assert.Equal((0x748u,0x74Fu),reopen.GetEndpoint(false));
            Assert.Equal("CanTool XML",reopen.Parameters.Single(p=>p.Name=="APPPhyID").Source);
        }
        finally
        {
            if(File.Exists(src))File.Delete(src);
            if(File.Exists(dst))File.Delete(dst);
        }
    }
    [Fact]
    public void XmlDtdIsNotAllowedAndNoReferenceDefaultsAreInjected()
    {
        string src=Path.GetTempFileName();
        try
        {
            File.WriteAllText(src,"<!DOCTYPE Configuration [ <!ENTITY xxe SYSTEM 'file:///etc/passwd'> ]>"+
                "<Configuration><APPPhyID>&xxe;</APPPhyID></Configuration>");
            Assert.Throws<XmlException>(()=>CanToolXmlProfileReader.Load(src));
            var cfg=new CanToolXmlProfileReader();
            Assert.All(cfg.Parameters,p=>Assert.Equal("",p.Value));
        }
        finally { File.Delete(src); }
    }
    [Fact]
    public void FirmwareBinRequiresExplicitBaseAndHonoursInclusiveUpperBound()
    {
        string bin=Path.Combine(Path.GetTempPath(),Guid.NewGuid().ToString("N")+".bin");
        try
        {
            File.WriteAllBytes(bin,[1,2,3,4]);
            var cfg=new CanToolXmlProfileReader();
            Assert.Throws<FormatException>(()=>FirmwareSelectionService.Load(bin,"",cfg));
            cfg.Set("FlashStartAddr","0x10008000");
            cfg.Set("FlashEndAddr","0x10008003");
            var image=FirmwareSelectionService.Load(bin,"0x10008000",cfg);
            Assert.Equal("BIN",image.Format);
            Assert.Equal(4L,image.Bytes);
            Assert.Equal(0x10008004UL,image.EndExclusive);
            cfg.Set("FlashEndAddr","0x10008002");
            Assert.Throws<FormatException>(()=>FirmwareSelectionService.Load(bin,"0x10008000",cfg));
        }
        finally { File.Delete(bin); }
    }
    [Fact]
    public void FirmwareHexRejectsBadChecksumRatherThanSkippingRecord()
    {
        string file=Path.Combine(Path.GetTempPath(),Guid.NewGuid().ToString("N")+".hex");
        try
        {
            File.WriteAllText(file,":0400000001020304F3\n:00000001FF\n");
            Assert.Throws<AutoTest.Specialists.Flash.FirmwareFormatException>(()=>
                FirmwareSelectionService.Load(file,null,new CanToolXmlProfileReader()));
        }
        finally { File.Delete(file); }
    }
    [Fact]
    public void RealCanToolChineseTags_AreImportedAndSavedWithoutEnglishDuplicates()
    {
        string src=Path.Combine(Path.GetTempPath(),Guid.NewGuid().ToString("N")+".xml");
        string dst=Path.Combine(Path.GetTempPath(),Guid.NewGuid().ToString("N")+".xml");
        try
        {
            // Mirrors the TAG STRUCTURE of the provided reference, not a real ECU profile.
            File.WriteAllText(src,"<root><BootloadPhyID>18DA50F1</BootloadPhyID>"+
                "<APPPhyID>747</APPPhyID><BootRespond>18DAF150</BootRespond>"+
                "<APPRespond>74F</APPRespond><VersionID>74F</VersionID>"+
                "<DiagnosticsID>18FE150E</DiagnosticsID>"+
                "<BootFeature><item>73</item></BootFeature>"+
                "<APPFeature><item>701</item></APPFeature>"+
                "<FLASH起始地址>10008000</FLASH起始地址>"+
                "<FLASH结束地址>1006FFFF</FLASH结束地址>"+
                "<波特率>500K</波特率></root>");
            var cfg=CanToolXmlProfileReader.Load(src);
            Assert.Equal("10008000",cfg.Get("FlashStartAddr"));
            Assert.Equal("1006FFFF",cfg.Get("FlashEndAddr"));
            Assert.Equal("500K",cfg.Get("BaudRate"));
            cfg.ValidateConfiguredRange();
            Assert.Contains("BootFeature/item = 73",cfg.UnmappedEntries);
            Assert.Contains("APPFeature/item = 701",cfg.UnmappedEntries);
            Assert.DoesNotContain("FLASH起始地址",cfg.UnmappedNames);
            Assert.DoesNotContain("FLASH结束地址",cfg.UnmappedNames);
            Assert.DoesNotContain("波特率",cfg.UnmappedNames);
            cfg.SaveAs(dst);
            var saved=XDocument.Load(dst);
            Assert.Equal("root",saved.Root!.Name.LocalName);
            Assert.Single(saved.Descendants("FLASH起始地址"));
            Assert.Single(saved.Descendants("FLASH结束地址"));
            Assert.Single(saved.Descendants("波特率"));
            Assert.Empty(saved.Descendants("FlashStartAddr"));
            Assert.Empty(saved.Descendants("FlashEndAddr"));
            Assert.Empty(saved.Descendants("BaudRate"));
            Assert.Equal("73",saved.Descendants("BootFeature").Single().Element("item")!.Value);
            Assert.Equal("701",saved.Descendants("APPFeature").Single().Element("item")!.Value);
            // Editing the canonical UI field must update its EXISTING Chinese tag.
            cfg.Set("FlashStartAddr","10009000");
            cfg.SaveAs(dst);
            saved=XDocument.Load(dst);
            Assert.Equal("10009000",saved.Descendants("FLASH起始地址").Single().Value);
            Assert.Empty(saved.Descendants("FlashStartAddr"));
        }
        finally
        {
            if(File.Exists(src))File.Delete(src);
            if(File.Exists(dst))File.Delete(dst);
        }
    }

    [Fact]
    public void LocalizedRangeIsEnforcedDuringOfflineImageReview()
    {
        string path=Path.Combine(Path.GetTempPath(),Guid.NewGuid().ToString("N")+".xml");
        string bin=Path.Combine(Path.GetTempPath(),Guid.NewGuid().ToString("N")+".bin");
        try
        {
            File.WriteAllText(path,
                "<root><FLASH起始地址>10008000</FLASH起始地址>"+
                "<FLASH结束地址>10008003</FLASH结束地址></root>");
            File.WriteAllBytes(bin,[1,2,3,4]);
            var cfg=CanToolXmlProfileReader.Load(path);
            Assert.Equal(4L,FirmwareSelectionService.Load(bin,"10008000",cfg).Bytes);
            Assert.Throws<FormatException>(()=>
                FirmwareSelectionService.Load(bin,"10008001",cfg));
        }
        finally
        {
            if(File.Exists(path))File.Delete(path);
            if(File.Exists(bin))File.Delete(bin);
        }
    }

    [Fact]
    public void DuplicateCanonicalAndChineseTags_AreNotSilentlyChosen()
    {
        string path=Path.GetTempFileName();
        try
        {
            File.WriteAllText(path,"<root><FlashStartAddr>10008000</FlashStartAddr>"+
                "<FLASH起始地址>1000A000</FLASH起始地址></root>");
            Assert.Throws<FormatException>(()=>CanToolXmlProfileReader.Load(path));
        }
        finally { File.Delete(path); }
    }


    [Fact]
    public void SafetyFileIsTheOnlyActiveFlashSecurityInput_LegacyFieldRoundTripsWithoutUse()
    {
        string source=Path.Combine(Path.GetTempPath(),Guid.NewGuid().ToString("N")+".xml");
        string saved=Path.Combine(Path.GetTempPath(),Guid.NewGuid().ToString("N")+".xml");
        try
        {
            File.WriteAllText(source,
                "<Configuration><SafetyFilePath>SafetyFile.dll</SafetyFilePath>"+
                "<SeedToKeyDll_Path>sa_demo.dll</SeedToKeyDll_Path></Configuration>");
            var config=CanToolXmlProfileReader.Load(source);
            Assert.Equal("SafetyFile.dll",config.Get("SafetyFilePath"));
            Assert.DoesNotContain(config.Parameters,x=>x.Name=="SeedToKeyDll_Path");
            Assert.Contains("SeedToKeyDll_Path",config.UnmappedNames);
            config.Set("SafetyFilePath","actual_safety.dll");
            config.SaveAs(saved);
            var restored=CanToolXmlProfileReader.Load(saved);
            Assert.Equal("actual_safety.dll",restored.Get("SafetyFilePath"));
            Assert.DoesNotContain(restored.Parameters,x=>x.Name=="SeedToKeyDll_Path");
            // Preserve unknown historical XML without loading it into FlashLab.
            var xml=XDocument.Load(saved);
            Assert.Single(xml.Descendants("SafetyFilePath"));
            Assert.Equal("sa_demo.dll",xml.Descendants("SeedToKeyDll_Path").Single().Value);
        }
        finally
        {
            if(File.Exists(source))File.Delete(source);
            if(File.Exists(saved))File.Delete(saved);
        }
    }
}

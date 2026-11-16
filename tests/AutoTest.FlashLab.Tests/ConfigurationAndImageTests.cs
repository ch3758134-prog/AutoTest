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
            Assert.Throws<FormatException>(()=>FirmwareSelectionService.Load(file,null,
                new CanToolXmlProfileReader()));
        }
        finally { File.Delete(file); }
    }
}

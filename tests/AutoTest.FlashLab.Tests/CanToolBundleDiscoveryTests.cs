using AutoTest.FlashLab.Configuration;
using Xunit;

namespace AutoTest.FlashLab.Tests;

/// <summary>Offline filesystem behavior only. No ECU, USB, or fabricated bus.</summary>
public sealed class CanToolBundleDiscoveryTests
{
    [Fact]
    public void CanToolThreeFileBundle_ImportsXmlFirmwareSafetyFromNestedFolder()
    {
        var folder=Directory.CreateTempSubdirectory("flash-three-");
        try
        {
            string cfg=Directory.CreateDirectory(Path.Combine(folder.FullName,"Cfg")).FullName;
            string firmware=Directory.CreateDirectory(Path.Combine(folder.FullName,"Firmware")).FullName;
            var xml=Path.Combine(cfg,"Configuration.xml");
            var image=Path.Combine(firmware,"ECU.hex");
            var safety=Path.Combine(folder.FullName,"SafetyFile.dll");
            File.WriteAllText(xml,"<root/>");
            File.WriteAllText(image,":00000001FF");
            File.WriteAllBytes(safety,[0x4D,0x5A]);
            // Optional native Seed/Key file is NOT a fourth mandatory input.
            File.WriteAllBytes(Path.Combine(folder.FullName,"sa_demo.dll"),[0x4D,0x5A]);
            var result=CanToolBundleDiscovery.Discover(folder.FullName);
            Assert.Equal(xml,result.ConfigurationXml);
            Assert.Equal(image,result.FirmwareImage);
            Assert.Equal(safety,result.ManagedSafetyFile);
        }
        finally {folder.Delete(true);}
    }

    [Fact]
    public void CanToolThreeFileBundle_NeverGuessesAmongDifferentFirmwareImages()
    {
        var folder=Directory.CreateTempSubdirectory("flash-ambiguous-");
        try
        {
            File.WriteAllText(Path.Combine(folder.FullName,"Configuration.xml"),"<root/>");
            File.WriteAllBytes(Path.Combine(folder.FullName,"SafetyFile.dll"),[0x4D,0x5A]);
            File.WriteAllText(Path.Combine(folder.FullName,"APP.hex"),":00000001FF");
            File.WriteAllText(Path.Combine(folder.FullName,"BOOT.hex"),":00000001FF");
            Assert.Throws<FormatException>(()=>CanToolBundleDiscovery.Discover(folder.FullName));
        }
        finally {folder.Delete(true);}
    }

    [Fact]
    public void SafetyFileIsAutomaticallyFoundBesideCanToolConfiguration()
    {
        var root=Directory.CreateTempSubdirectory("flash-sibling-");
        try
        {
            string cfg=Directory.CreateDirectory(Path.Combine(root.FullName,"Cfg")).FullName;
            string xml=Path.Combine(cfg,"Configuration.xml");
            string safety=Path.Combine(cfg,"SafetyFile.dll");
            File.WriteAllText(xml,"<Configuration/>");
            File.WriteAllBytes(safety,[0x4D,0x5A]);
            Assert.Equal(safety,CanToolBundleDiscovery.FindSafetyFileNear(xml,null));
        }
        finally{root.Delete(true);}
    }

    [Fact]
    public void SafetyFileLookupDoesNotUseUnrelatedDllOrScanArbitraryTree()
    {
        var root=Directory.CreateTempSubdirectory("flash-notfound-");
        try
        {
            string xml=Path.Combine(root.FullName,"Configuration.xml");
            File.WriteAllText(xml,"<Configuration/>");
            File.WriteAllBytes(Path.Combine(root.FullName,"sa_demo.dll"),[0x4D,0x5A]);
            Assert.Null(CanToolBundleDiscovery.FindSafetyFileNear(xml,null));
        }
        finally{root.Delete(true);}
    }

    [Fact]
    public void CanToolThreeFileBundle_RequiresAllThreeFiles()
    {
        var folder=Directory.CreateTempSubdirectory("flash-missing-");
        try
        {
            File.WriteAllText(Path.Combine(folder.FullName,"Configuration.xml"),"<root/>");
            File.WriteAllText(Path.Combine(folder.FullName,"ECU.hex"),":00000001FF");
            Assert.Throws<FileNotFoundException>(()=>CanToolBundleDiscovery.Discover(folder.FullName));
        }
        finally {folder.Delete(true);}
    }
}

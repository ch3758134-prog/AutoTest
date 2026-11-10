using System.Text;
using AutoTest.Specialists.Flash;
using Xunit;
using System.Xml;

namespace AutoTest.Specialists.Tests.Flash;

public sealed class CanToolConfigurationImporterTests
{
    private const string Sample = """
        <Configuration>
          <BootloadPhyID>18DA50F1</BootloadPhyID>
          <BootRespond>18DAF150</BootRespond>
          <APPPhyID>747</APPPhyID>
          <APPRespond>74F</APPRespond>
          <FlashStartAddr>10008000</FlashStartAddr>
          <FlashEndAddr>1006FFFF</FlashEndAddr>
          <BaudRate>500K</BaudRate>
          <BootFeature>73</BootFeature>
        </Configuration>
        """;

    [Fact]
    public void Imported_Xml_Uses_Explicit_App_And_Boot_Ids_And_Inclusive_Length()
    {
        var p = CanToolConfigurationImporter.Parse(Encoding.UTF8.GetBytes(Sample));
        Assert.Equal(0x18DA50F1u, p.BootRequestId);
        Assert.Equal(0x18DAF150u, p.BootResponseId);
        Assert.Equal(0x747u, p.AppRequestId);
        Assert.Equal(0x74Fu, p.AppResponseId);
        Assert.Equal(0x68000UL, p.FlashLength);
        Assert.Contains(p.Fields, x => x.Field == "BlockLength" && x.Source == "XML 缺失");
        Assert.Contains(p.Fields, x => x.Field == "P2TimeoutMs" &&
            x.Source == "原版 CanTool XML Loader 不读取");
    }
    [Fact]
    public void Invalid_Request_Response_Mapping_Is_Rejected()
    {
        var text = Sample.Replace("<APPRespond>74F</APPRespond>", "<APPRespond>747</APPRespond>");
        Assert.Throws<FormatException>(() => CanToolConfigurationImporter.Parse(Encoding.UTF8.GetBytes(text)));
    }
    [Fact]
    public void Dtd_Entities_Are_Rejected()
    {
        string text = "<!DOCTYPE x [<!ENTITY leak SYSTEM 'file:///C:/Windows/win.ini'>]>" + Sample;
        Assert.ThrowsAny<XmlException>(() => CanToolConfigurationImporter.Parse(Encoding.UTF8.GetBytes(text)));
    }
    [Fact]
    public void No_Default_Target_Can_Ids()
    {
        string text = Sample.Replace("<APPPhyID>747</APPPhyID>", "");
        Assert.Throws<FormatException>(() => CanToolConfigurationImporter.Parse(Encoding.UTF8.GetBytes(text)));
    }
}

using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using AutoTest.Devices.Usb;
using AutoTest.FlashLab.Configuration;
using AutoTest.FlashLab.Firmware;

namespace AutoTest.FlashLab.Presentation;

public sealed class FlashLabViewModel : INotifyPropertyChanged
{
    private string _status="离线：无现场发送许可", _configPath="未载入配置",
        _unknown="未知字段：未载入",_firmwarePath="未选择固件",
        _imageSummary="固件文件未校验",_imageRanges="";
    private string _requestId="", _responseId="", _did="", _confirmation="",_didResult="未执行";
    private WinUsbCandidate? _selectedDevice;
    private bool _isBusy;

    public ObservableCollection<CanToolParameter> Parameters { get; } = [];
    public ObservableCollection<WinUsbCandidate> Candidates { get; } = [];
    public ObservableCollection<string> Logs { get; } = [];
    public CanToolXmlProfileReader Configuration { get; private set; } = new();
    public FirmwareSelection? Image { get; private set; }
    public event PropertyChangedEventHandler? PropertyChanged;
    private void Changed([CallerMemberName] string? name=null) =>
        PropertyChanged?.Invoke(this,new PropertyChangedEventArgs(name));
    private void Set(ref string target,string value,[CallerMemberName] string? name=null)
    { if(target==value)return;target=value;Changed(name); }

    public FlashLabViewModel() => ApplyConfiguration(new());
    public string Status { get=>_status; set=>Set(ref _status,value); }
    public string ConfigurationPath { get=>_configPath; set=>Set(ref _configPath,value); }
    public string Unknown { get=>_unknown; set=>Set(ref _unknown,value); }
    public string FirmwarePath { get=>_firmwarePath; set=>Set(ref _firmwarePath,value); }
    public string ImageSummary { get=>_imageSummary; set=>Set(ref _imageSummary,value); }
    public string ImageRanges { get=>_imageRanges; set=>Set(ref _imageRanges,value); }
    public string DidResult { get=>_didResult; set=>Set(ref _didResult,value); }
    public string RequestId { get=>_requestId; set{Set(ref _requestId,value);Changed(nameof(ExpectedConsent));} }
    public string ResponseId { get=>_responseId; set{Set(ref _responseId,value);Changed(nameof(ExpectedConsent));} }
    public string Did { get=>_did; set{Set(ref _did,value);Changed(nameof(ExpectedConsent));} }
    public string TypedConfirmation { get=>_confirmation; set=>Set(ref _confirmation,value); }
    public bool IsBusy { get=>_isBusy; set { _isBusy=value; Changed(); } }
    public WinUsbCandidate? SelectedDevice
    {get=>_selectedDevice; set{_selectedDevice=value;Changed();}}

    public string ExpectedConsent
    {
        get
        {
            try
            {
                uint tx=CanToolXmlProfileReader.ParseHexUInt(RequestId,"请求 ID");
                uint rx=CanToolXmlProfileReader.ParseHexUInt(ResponseId,"响应 ID");
                ushort did=CanToolXmlProfileReader.ParseDid(Did);
                return PhysicalCommandFence.ReadDidConfirmation(tx,rx,did);
            }
            catch{return "先填写 CAN1 请求/响应 ID 与 DID；现场授权不能自动授予。";}
        }
    }
    public void ApplyConfiguration(CanToolXmlProfileReader configuration)
    {
        Configuration=configuration;
        Parameters.Clear();
        foreach(var p in configuration.Parameters) Parameters.Add(p);
        ConfigurationPath=configuration.SourcePath??"未载入配置（可编辑后另存为 XML）";
        Unknown="未映射 XML 元素："+(configuration.UnmappedNames.Count==0
            ?"无 / 尚未导入":string.Join("、",configuration.UnmappedNames));
    }
    public void SetImage(string file,FirmwareSelection image)
    {
        Image=image;FirmwarePath=file;ImageSummary=image.Summary;ImageRanges=image.AddressRanges;
    }
    public void AddLog(string line)
    {
        Logs.Add(line);
        while(Logs.Count>1200) Logs.RemoveAt(0);
    }
}

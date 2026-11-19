using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using Prism.Commands;
using Prism.Mvvm;

namespace AutoTest.Desktop.Shell;

public sealed partial class ManualBenchViewModel
{
    public sealed class LedChannel(byte number, string connector) : BindableBase
    {
        private string _gpioState = "未读取";
        public byte Number { get; } = number;
        public string Label => "LED" + Number.ToString(CultureInfo.InvariantCulture);
        public string Connector { get; } = connector;
        public string HighSide => "HOUT" + (Number + 1);
        public string GpioState
        {
            get => _gpioState;
            private set => SetProperty(ref _gpioState, value);
        }
        internal void Update(bool? level) => GpioState = level switch
        {
            true => "GPIO HIGH",
            false => "GPIO LOW",
            _ => "UNKNOWN"
        };
    }

    public ObservableCollection<LedChannel> LedChannels { get; } = new(
    [
        new(1, "C11"), new(2, "C25"), new(3, "C26"),
        new(4, "C6"), new(5, "C7"), new(6, "C8")
    ]);

    public DelegateCommand ReadLedsCommand { get; private set; } = null!;
    public bool CanReadLedGpio => !IsBusy && _hardware.IsConnected && !_hardware.IsTestRunApproved;
    public bool CanControlLeds => CanReadLedGpio && _hardware.HasLedGpioContract;
    public string LedStatusText => CanControlLeds
        ? "已确认 0x1B 读回；仅表示 MCU GPIO，不表示实际输出电压"
        : "尚未验证六路 LED 指令；先点击“读取 LED GPIO”";

    private void InitializeLedControls()
    {
        ReadLedsCommand = new DelegateCommand(async () => await ReadLedGpioAsync(),
            () => CanReadLedGpio);
    }

    private void ResetLedReadback()
    {
        foreach (var led in LedChannels) led.Update(null);
        NotifyLedCommands();
    }

    private void NotifyLedCommands()
    {
        RaisePropertyChanged(nameof(CanReadLedGpio));
        RaisePropertyChanged(nameof(CanControlLeds));
        RaisePropertyChanged(nameof(LedStatusText));
        ReadLedsCommand?.RaiseCanExecuteChanged();
    }

    private void SetLedReadback(byte mask)
    {
        for (var i = 0; i < LedChannels.Count; i++)
            LedChannels[i].Update((mask & (1 << i)) != 0);
        NotifyLedCommands();
    }

    private async Task ReadLedGpioAsync() => await Execute("读取 LED GPIO", async () =>
    {
        if (!_hardware.IsConnected) throw new IOException("请先连接 USB 主控。");
        try
        {
            var mask = await _hardware.ReadLedLevelsAsync();
            SetLedReadback(mask);
            Append(new BenchLogEntry(DateTime.Now.ToString("HH:mm:ss.fff"),
                "IO LED", "READ", "0x1B", mask.ToString("X2"),
                "本次连接的六路 MCU GPIO 位图；非负载电压/LED 状态"));
        }
        catch
        {
            ResetLedReadback();
            throw;
        }
    });

    public async Task ApplyLedSelectionAsync(byte index, bool on) => await Execute(
        "LED" + index.ToString(CultureInfo.InvariantCulture), async () =>
        {
            if (index is < 1 or > 6) throw new ArgumentOutOfRangeException(nameof(index));
            // Clicks target exactly one output. Never send a six-channel mask
            // here: an unrelated channel may hold an externally driven load.
            var channel = LedChannels[index - 1];
            channel.Update(null);
            var confirmation = "AUTHORIZE LED " + index + (on ? " ON" : " OFF");
            try
            {
                await _hardware.SendManualLedAsync(index, on, confirmation);
                Append(new BenchLogEntry(DateTime.Now.ToString("HH:mm:ss.fff"),
                    "IO LED", "SET ACK", "0x1A", index.ToString("X2") + (on ? "01" : "00"),
                    channel.Label + " 主控执行 ACK；外部电压尚未观测"));
                try
                {
                    var mask = await _hardware.ReadLedLevelsAsync();
                    SetLedReadback(mask);
                    Append(new BenchLogEntry(DateTime.Now.ToString("HH:mm:ss.fff"),
                        "IO LED", "READ", "0x1B", mask.ToString("X2"),
                        channel.Label + " 操作后 GPIO 回读"));
                    if (((mask & (1 << (index - 1))) != 0) != on)
                        Status = channel.Label + " GPIO 回读与请求不一致！请核对台架。";
                }
                catch (Exception error)
                {
                    channel.Update(null);
                    Status = channel.Label + " 请求已 ACK，但 GPIO 回读失败：" + error.Message;
                }
            }
            catch
            {
                channel.Update(null);
                throw;
            }
        });
}

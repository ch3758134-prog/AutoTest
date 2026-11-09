using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using AutoTest.Specialists.Flash;
using Microsoft.Win32;

namespace AutoTest.Desktop.Shell;

/// <summary>
/// File-based firmware inspection. This parses the operator's actual firmware
/// bytes with the production parser. It has no Bootloader client and cannot
/// claim an ECU was flashed or that ECU-specific preflight passed.
/// </summary>
public sealed class RealFirmwareImageLabWindow : Window
{
    private readonly TextBox _baseAddress = new()
    {
        Text = "0x00000000", Width = 132, Margin = new Thickness(6, 0, 8, 0)
    };
    private readonly TextBox _filePath = new()
    {
        IsReadOnly = true, MinWidth = 440, Margin = new Thickness(8, 0)
    };
    private readonly TextBlock _result = new()
    {
        Text = "尚未选择镜像。仅验证文件完整性与地址结构；未执行 ECU 刷写。",
        TextWrapping = TextWrapping.Wrap, Margin = new Thickness(12, 8, 12, 8)
    };
    private readonly DataGrid _segments = new()
    {
        IsReadOnly = true, AutoGenerateColumns = true,
        CanUserAddRows = false, Margin = new Thickness(12),
        EnableRowVirtualization = true
    };

    public RealFirmwareImageLabWindow()
    {
        Title = "AutoTest · 固件镜像检查 · 真实文件";
        WindowState = WindowState.Maximized;
        MinWidth = 840; MinHeight = 550;
        Background = Brushes.White;
        FontFamily = new FontFamily("Segoe UI"); FontSize = 13;

        var layout = new Grid();
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        var panel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(12) };
        panel.Children.Add(new TextBlock
        {
            Text = "固件文件", VerticalAlignment = VerticalAlignment.Center
        });
        panel.Children.Add(_filePath);
        var browse = new Button { Content = "选择 HEX / S19 / BIN…", Padding = new Thickness(10, 5) };
        browse.Click += Browse;
        panel.Children.Add(browse);
        panel.Children.Add(new TextBlock
        {
            Text = "BIN 起始地址", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(14, 0, 0, 0)
        });
        panel.Children.Add(_baseAddress);
        Grid.SetRow(panel, 0);
        layout.Children.Add(panel);

        Grid.SetRow(_result, 1);
        layout.Children.Add(_result);

        Grid.SetRow(_segments, 2);
        layout.Children.Add(_segments);
        Content = layout;
    }

    private void Browse(object sender, RoutedEventArgs e)
    {
        var picker = new OpenFileDialog
        {
            Title = "选择真实 ECU 固件镜像",
            Filter = "固件镜像 (*.hex;*.ihex;*.s19;*.s28;*.s37;*.srec;*.mot;*.bin)|*.hex;*.ihex;*.s19;*.s28;*.s37;*.srec;*.mot;*.bin|所有文件|*.*",
            CheckFileExists = true, Multiselect = false
        };
        if (picker.ShowDialog(this) != true) return;

        // Clear prior result before accepting the next file, including on failure.
        _filePath.Text = picker.FileName;
        _segments.ItemsSource = null;
        _result.Text = "读取中…";

        try
        {
            var file = new FileInfo(picker.FileName);
            if (file.Length == 0 || file.Length > FirmwareImageParser.MaxImageBytes)
                throw new InvalidDataException("固件文件为空或超过 16 MiB 文件上限。");
            var bytes = File.ReadAllBytes(file.FullName);
            var extension = file.Extension.ToLowerInvariant();
            FirmwareImage image;
            if (extension is ".hex" or ".ihex")
                image = FirmwareImageParser.ParseHex(File.ReadAllText(file.FullName));
            else if (extension is ".s19" or ".s28" or ".s37" or ".srec" or ".mot")
                image = FirmwareImageParser.ParseS19(File.ReadAllText(file.FullName));
            else if (extension == ".bin")
            {
                var address = _baseAddress.Text.Trim().Replace("0x", "", StringComparison.OrdinalIgnoreCase);
                if (!uint.TryParse(address, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var baseAddress))
                    throw new FormatException("BIN 起始地址须为 32 位十六进制数。");
                image = FirmwareImageParser.ParseBin(bytes, baseAddress);
            }
            else throw new InvalidDataException("不支持的镜像扩展名。");

            var addressRows = image.Segments.Select((segment, index) => new
            {
                序号 = index + 1,
                起始地址 = $"0x{segment.Address:X8}",
                结束地址_不含 = $"0x{segment.EndExclusive:X8}",
                数据字节数 = segment.Data.Length
            }).ToArray();
            _segments.ItemsSource = addressRows;
            _result.Text = $"真实文件：{file.Name}   格式：{image.Format}   有效数据：{image.ByteCount:N0} 字节   " +
                $"地址段：{image.Segments.Length}   SHA-256：{Convert.ToHexString(SHA256.HashData(bytes))}\n" +
                "文件结构已解析；尚未匹配 ECU/Bootloader/内存分区，也未完成烧录预检或物理刷写。";
        }
        catch (Exception ex)
        {
            _result.Text = $"镜像检查失败：{ex.GetType().Name} · {ex.Message}";
        }
    }
}

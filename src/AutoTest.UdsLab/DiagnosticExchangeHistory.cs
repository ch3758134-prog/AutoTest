using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows.Controls;

namespace AutoTest.UdsLab;

/// <summary>One real UDS request attempt and its observed outcome. No synthetic ECU facts.</summary>
public sealed record DiagnosticExchangeRow(DateTimeOffset Utc, Guid CorrelationId,
    string ActionName, string RequestHex, string ResponseHex,
    string Parsed, string Outcome, string Detail, double DurationMs)
{
    public string LocalTime => Utc.ToLocalTime().ToString("HH:mm:ss.fff");
    public string Duration => DurationMs.ToString("0", CultureInfo.InvariantCulture) + " ms";
}

public partial class MainWindow
{
    private readonly ObservableCollection<DiagnosticExchangeRow> _exchangeHistory = [];

    private void InitializeExchangeHistory() => ExchangeList.ItemsSource = _exchangeHistory;

    private void RecordExchange(string name, byte[] request, string response, string parsed,
        string outcome, string detail, DateTimeOffset started, Guid correlation)
    {
        var row = new DiagnosticExchangeRow(DateTimeOffset.UtcNow, correlation, name,
            Convert.ToHexString(request), response, parsed, outcome, detail,
            (DateTimeOffset.UtcNow - started).TotalMilliseconds);
        _exchangeHistory.Add(row);
        ExchangeList.SelectedItem = row;
        ExchangeList.ScrollIntoView(row);
    }

    private void Exchange_Selected(object sender, SelectionChangedEventArgs e)
    {
        if (ExchangeList.SelectedItem is not DiagnosticExchangeRow row) return;
        ResultStatus.Text = row.Outcome + " · " + row.ActionName;
        ResultStatus.Foreground = row.Outcome.StartsWith("收到 ECU 正响应", StringComparison.Ordinal)
            ? System.Windows.Media.Brushes.ForestGreen
            : row.Outcome.Contains("否定响应", StringComparison.Ordinal) ||
              row.Outcome.Contains("不匹配", StringComparison.Ordinal) ||
              row.Outcome.Contains("取消", StringComparison.Ordinal)
                ? System.Windows.Media.Brushes.DarkOrange
                : System.Windows.Media.Brushes.DarkRed;
        LastRequestBox.Text = row.RequestHex;
        LastResponseBox.Text = row.ResponseHex;
        ParsedBox.Text = row.Parsed;
        DetailBox.Text = row.Detail + "\n请求关联 ID：" + row.CorrelationId;
    }
}

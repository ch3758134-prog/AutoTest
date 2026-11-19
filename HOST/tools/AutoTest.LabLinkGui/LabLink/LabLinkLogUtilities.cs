using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace AutoTest.LabLinkGui.LabLink;

public enum LabLogFilter { All, Operations, Communications, Errors }

public static class LabLinkLogUtilities
{
    public static bool Matches(LabLinkLogEntry entry, LabLogFilter filter, string? search)
    {
        bool inCategory = filter switch
        {
            LabLogFilter.Operations => entry.Kind is LabLinkActivityKind.UserAction or LabLinkActivityKind.Discovery or LabLinkActivityKind.Status or LabLinkActivityKind.Success,
            LabLogFilter.Communications => entry.Kind is LabLinkActivityKind.Request or LabLinkActivityKind.Response or LabLinkActivityKind.Trace,
            LabLogFilter.Errors => entry.Kind is LabLinkActivityKind.Failure or LabLinkActivityKind.Error,
            _ => true
        };
        if (!inCategory) return false;
        if (string.IsNullOrWhiteSpace(search)) return true;
        var term = search.Trim();
        return new[] { entry.Message, entry.DeviceIdentity, entry.Stage, entry.Win32Text,
            entry.Sequence, entry.RawHex, entry.Trace }.Any(x => x.Contains(term, StringComparison.OrdinalIgnoreCase));
    }

    public static string Export(IEnumerable<LabLinkLogEntry> entries)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Time\tType\tMessage\tDevice\tStage\tWin32\tSEQ\tRawHex\tTrace");
        foreach (var e in entries)
        {
            string Clean(string s) => s.Replace("\r", "\\r").Replace("\n", "\\n").Replace("\t", " ");
            sb.AppendLine(string.Join("\t", new[] { e.TimeText, e.KindText, Clean(e.Message),
                Clean(e.DeviceIdentity), Clean(e.Stage), e.Win32Text, Clean(e.Sequence),
                Clean(e.RawHex), Clean(e.Trace) }));
        }
        return sb.ToString();
    }
}


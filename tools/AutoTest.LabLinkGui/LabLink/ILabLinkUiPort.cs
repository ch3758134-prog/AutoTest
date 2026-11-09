using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace AutoTest.LabLinkGui.LabLink;

/// <summary>UI-only boundary implemented by INTEGRATION using the existing DEVICE USB session.
/// Do not create a second USB discovery/session/protocol implementation here.</summary>
public interface ILabLinkUiPort
{
    event EventHandler<LabLinkUiEvent>? Activity;
    Task<IReadOnlyList<LabBoardIdentity>> DiscoverMatchingBoardsAsync(CancellationToken cancellationToken);
    Task ConnectAsync(LabBoardIdentity board, CancellationToken cancellationToken);
    Task DisconnectAsync(CancellationToken cancellationToken);
    bool IsConnected { get; }
    LabBoardIdentity? ConnectedBoard { get; }
}

public sealed record LabBoardIdentity(string DisplayName, string DevicePath, string? VidPid,
    string? Serial, string? Manufacturer = null)
{
    /// <summary>Disambiguates matching boards; raw DevicePath remains in the selected identity details.</summary>
    public string ChoiceLabel => $"{DisplayName} | {VidPid ?? "VID/PID未知"} | SN:{Serial ?? "未知"} | {DevicePath}";
}

public enum LabLinkActivityKind { UserAction, Discovery, Request, Response, Success, Failure, Error, Status, Trace }

/// <summary>Message content and Trace originate from the existing DEVICE/INTEGRATION event stream.</summary>
public sealed record LabLinkUiEvent(DateTimeOffset Timestamp, LabLinkActivityKind Kind,
    string Message, string? DeviceIdentity = null, string? Stage = null,
    int? Win32Error = null, string? Trace = null, string? Sequence = null,
    string? RawHex = null);


using System.Collections.Immutable;
using System.Security.Cryptography;
using AutoTest.Core;

namespace AutoTest.Specialists.Flash;

public sealed record FirmwareSegment(uint Address, ImmutableArray<byte> Data)
{
    public ulong EndExclusive => (ulong)Address + (ulong)Data.Length;
}
public sealed record FirmwareImage(string Format, ImmutableArray<FirmwareSegment> Segments,
    RevisionFingerprint SourceFingerprint)
{
    public long ByteCount => Segments.Sum(x => (long)x.Data.Length);
}
public sealed class FirmwareFormatException : FormatException
{
    public FirmwareFormatException(string message) : base(message) { }
}

// Parsers read input bytes; they do not interact with an ECU, filesystem or station.
public static class FirmwareImageParser
{
    public const int MaxImageBytes = 16 * 1024 * 1024;
    public static FirmwareImage ParseHex(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var segments = new List<FirmwareSegment>();
        ulong baseAddress = 0; bool eof = false;
        var lines = text.Replace("\r","").Split('\n');
        foreach (var sourceLine in lines)
        {
            var line = sourceLine.Trim(); if (line.Length == 0) continue;
            if (eof) throw new FirmwareFormatException("Record found after Intel HEX EOF.");
            if (!line.StartsWith(':')) throw new FirmwareFormatException("Intel HEX must start with ':'.");
            var data = DecodeHex(line[1..]);
            if (data.Length < 5 || data.Length != data[0] + 5 || data.Aggregate(0,(a,b)=>a+b)%256 != 0)
                throw new FirmwareFormatException("Intel HEX count/checksum mismatch.");
            var count = data[0]; int offset = (data[1]<<8)|data[2]; byte type = data[3];
            var bytes = data.AsSpan(4,count);
            switch(type)
            {
                case 0:
                    if (count == 0) break;
                    Add(segments,baseAddress + (ulong)offset,bytes); break;
                case 1:
                    if (count != 0 || offset != 0) throw new FirmwareFormatException("Invalid Intel HEX EOF record.");
                    eof = true; break;
                case 2:
                    if (count != 2 || offset != 0) throw new FirmwareFormatException("Invalid segment-base record.");
                    baseAddress = (ulong)((bytes[0]<<8)|bytes[1]) << 4; break;
                case 4:
                    if (count != 2 || offset != 0) throw new FirmwareFormatException("Invalid linear-base record.");
                    baseAddress = (ulong)((bytes[0]<<8)|bytes[1]) << 16; break;
                case 3: case 5:
                    if (count != 4 || offset != 0) throw new FirmwareFormatException("Invalid entry-point record.");
                    break; // Start PC is not an instruction to execute.
                default: throw new FirmwareFormatException($"Unsupported Intel HEX record type {type:X2}.");
            }
        }
        if (!eof) throw new FirmwareFormatException("Intel HEX missing EOF.");
        return Create("IntelHEX",segments,System.Text.Encoding.UTF8.GetBytes(text));
    }
    public static FirmwareImage ParseS19(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var segments = new List<FirmwareSegment>();
        bool terminated = false; int dataRecords = 0; int? declaredRecords = null;
        foreach(var sourceLine in text.Replace("\r","").Split('\n'))
        {
            string line = sourceLine.Trim(); if (line.Length == 0) continue;
            if (terminated) throw new FirmwareFormatException("S-record after termination.");
            if (line.Length < 4 || line[0] != 'S') throw new FirmwareFormatException("Invalid S-record line.");
            char type = line[1]; int addrLen = type switch
            {
                '0' or '1' or '5' or '9' => 2,
                '2' or '6' or '8' => 3,
                '3' or '7' => 4,
                _ => throw new FirmwareFormatException($"Unsupported S-record type {type}.")
            };
            var data = DecodeHex(line[2..]);
            if (data.Length < addrLen + 2 || data.Length != data[0] + 1
                || (data.Aggregate(0,(a,b)=>a+b) & 0xFF) != 0xFF)
                throw new FirmwareFormatException("S-record count/checksum mismatch.");
            ulong address = 0;
            for(int i = 0; i < addrLen; i++) address = (address<<8) | data[i+1];
            int len = data[0] - addrLen - 1;
            if (type is '1' or '2' or '3')
            {
                if (len > 0) Add(segments,address,data.AsSpan(1+addrLen,len));
                dataRecords++;
            }
            else if (type is '5' or '6')
            {
                if (len != 0 || declaredRecords.HasValue) throw new FirmwareFormatException("Invalid S-record count.");
                declaredRecords = checked((int)address);
            }
            else if (type is '7' or '8' or '9')
            {
                if (len != 0) throw new FirmwareFormatException("Invalid S-record terminator.");
                terminated = true;
            }
        }
        if (!terminated) throw new FirmwareFormatException("S-record missing terminator.");
        if (declaredRecords.HasValue && declaredRecords.Value != dataRecords)
            throw new FirmwareFormatException("S-record declared count mismatch.");
        return Create("SREC",segments,System.Text.Encoding.UTF8.GetBytes(text));
    }
    public static FirmwareImage ParseBin(ReadOnlySpan<byte> data, uint baseAddress)
    {
        var bytes = data.ToArray();
        var segments = new List<FirmwareSegment>();
        if (bytes.Length > 0) Add(segments,baseAddress,bytes);
        return Create("BIN",segments,bytes);
    }
    public static ImmutableArray<FirmwareSegment> ValidateSegments(IEnumerable<FirmwareSegment> input)
    {
        ArgumentNullException.ThrowIfNull(input);
        var segments = input.OrderBy(s=>s.Address).ToArray();
        if (segments.Length == 0) throw new FirmwareFormatException("Firmware image contains no data.");
        long size = 0; ulong end = 0;
        foreach(var segment in segments)
        {
            if (segment.Data.IsDefaultOrEmpty || segment.EndExclusive > 0x100000000UL)
                throw new FirmwareFormatException("Empty or out-of-range firmware segment.");
            if (end > segment.Address) throw new FirmwareFormatException("Firmware address overlap.");
            size += segment.Data.Length; end = segment.EndExclusive;
            if (size > MaxImageBytes) throw new FirmwareFormatException("Image exceeds configured memory bound.");
        }
        return segments.ToImmutableArray();
    }
    private static FirmwareImage Create(string format,List<FirmwareSegment> segments,byte[] original) =>
        new(format,ValidateSegments(segments),RevisionFingerprint.FromBytes(original));
    private static void Add(List<FirmwareSegment> segments,ulong address,ReadOnlySpan<byte> bytes)
    {
        if (address > uint.MaxValue || address + (ulong)bytes.Length > 0x100000000UL)
            throw new FirmwareFormatException("Firmware address overflow.");
        segments.Add(new((uint)address,ImmutableArray.CreateRange(bytes.ToArray())));
    }
    private static byte[] DecodeHex(string text)
    {
        if (text.Length % 2 != 0) throw new FirmwareFormatException("Odd-length hex record.");
        try { return Convert.FromHexString(text); }
        catch (FormatException) { throw new FirmwareFormatException("Invalid hex record characters."); }
    }
}

# FW-KEY-WS.zip — archived byte-exact source ZIP

**Status:** GitHub file is segmented solely because the GitHub connector could not transmit an 8 MB Base64 payload as one object. All five chunk Git blobs are binary, not Base64 text.

The exact original file is reconstructed by concatenating parts in lexicographic order, without any conversion.

- Original file: `FW-KEY-WS.zip`
- Original length: **6,025,867 bytes**
- Original SHA-256: `ce032e5063d09f7d4fdce9dd190a8284bbf02ecbbd0258936d8f48387603e93d`
- Original Git blob SHA-1: `ae0b9b788713f1f808a28243e5235e1a88918743`
- Source provenance: `AutoTest_G0_唯一图纸与现役固件事实基线_v0.2_待审.zip / evidence/WS.zip`; bytewise identical in all 5,651 paths to `KEY_SIM.zip / KEY_SIM/WS/` after normalizing prefix.

## PowerShell recovery

Run from the folder containing these files:

```powershell
$parts = Get-ChildItem FW-KEY-WS.zip.part* | Sort-Object Name
$dst = [System.IO.File]::Create((Join-Path (Get-Location) "FW-KEY-WS.zip"))
try {
    foreach ($part in $parts) {
        $inputStream = [System.IO.File]::OpenRead($part.FullName)
        try { $inputStream.CopyTo($dst) } finally { $inputStream.Dispose() }
    }
} finally { $dst.Dispose() }
(Get-FileHash FW-KEY-WS.zip -Algorithm SHA256).Hash.ToLowerInvariant()
```

Expected hash is the SHA-256 above. Do not flash or treat this archive as an installed firmware image; this is a frozen source input only.

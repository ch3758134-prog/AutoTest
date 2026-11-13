# FW-MAIN-Project.zip — exact source ZIP split for GitHub connector transfer

- Original source archive: `FW-MAIN-Project.zip`, 17,529,847 bytes
- SHA-256: `34b7e11b7d5d0fbb7b2178070f52e41febd88c15033a215516084ca294900239`
- Git blob SHA-1 if recovered intact: `0fbda0995ca7dff9afe1c8173488310ef5049c89`
- Source: previously archived `evidence/Project.zip` within `AutoTest_G0_唯一图纸与现役固件事实基线_v0.2_待审.zip`; 726/726 `Project/` contents match the relevant subtree of `EPB_Test_Keil.zip`.
- Chunks: `FW-MAIN-Project.zip.part000` through `part011` (11×1,500,000 bytes plus 1,029,847 bytes). Each file is raw binary, **not base64**; combine in ascending suffix order, with no separator.
- This is a frozen source asset, **not** an installed firmware image or a hardware release approval.

PowerShell recovery when in this directory:

```powershell
$dest=[IO.File]::Create((Join-Path (Get-Location) "FW-MAIN-Project.zip"))
try {
  Get-ChildItem "FW-MAIN-Project.zip.part*" | Sort-Object Name | ForEach-Object {
    $src=[IO.File]::OpenRead($_.FullName)
    try { $src.CopyTo($dest) } finally { $src.Dispose() }
  }
} finally { $dest.Dispose() }
(Get-FileHash "FW-MAIN-Project.zip" -Algorithm SHA256).Hash.ToLowerInvariant()
```

The result must match the SHA-256 above before treating the archive as recovered.

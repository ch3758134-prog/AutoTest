using System.IO;
using AutoTest.FlashLab.Configuration;
using AutoTest.FlashLab.Programming;
using Xunit;

namespace AutoTest.Desktop.Tests.Projects;

public sealed class NativeFlashProfileTests
{
    [Fact]
    public void FirmwareDisplayName_UsesOriginalNameAndNeverShowsInternalHash()
    {
        var hash = new string('7', 64);
        var draft = NativeFlashProfileDocument.CreateDraft("HYDEPB") with
        {
            FirmwareRelativePath = "assets/flash/firmware/" + hash + ".hex",
            FirmwareSha256 = hash
        };
        Assert.Equal("已导入 HEX（原始文件名未记录）", draft.GetFirmwareDisplayLabel());
        var named = draft with { FirmwareDisplayName = "HydEPB_APP_V2.hex" };
        Assert.Equal("HydEPB_APP_V2.hex", named.GetFirmwareDisplayLabel());
        Assert.Equal("尚未选择 HEX", NativeFlashProfileDocument
            .CreateDraft("HYDEPB").GetFirmwareDisplayLabel());
        Assert.Equal(draft.FirmwareRelativePath, named.FirmwareRelativePath);
        Assert.Equal(draft.FirmwareSha256, named.FirmwareSha256);
    }

    [Fact]
    public async Task AModelHasOneStableNativeProfileWithRevisionConflictProtection()
    {
        var root = Path.Combine(Path.GetTempPath(), "autotest-native-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var draft = NativeFlashProfileDocument.CreateDraft("EPBA");
            Assert.Null(await NativeFlashProfileStore.LoadAsync(root));
            var first = await NativeFlashProfileStore.SaveDraftAsync(root, draft, null);
            Assert.Equal(1, first.Profile.Revision);
            Assert.Equal(draft.ProfileId, first.Profile.ProfileId);
            var second = await NativeFlashProfileStore.SaveDraftAsync(root,
                first.Profile with { EcuCanKbps = 250 }, first.ProfileSha256);
            Assert.Equal(2, second.Profile.Revision);
            Assert.Equal("EPBA", second.Profile.EcuModelCode);
            Assert.Equal(250, (await NativeFlashProfileStore.LoadAsync(root))!.Profile.EcuCanKbps);
            await Assert.ThrowsAsync<IOException>(() =>
                NativeFlashProfileStore.SaveDraftAsync(root, first.Profile, first.ProfileSha256));
            Assert.True(File.Exists(NativeFlashProfileStore.FullProfilePath(root) + ".bak"));
            Assert.Equal(first.Profile.ProfileId, second.Profile.ProfileId);
        }
        finally { Directory.Delete(root, true); }
    }

    [Theory]
    [InlineData("../outside.dll")]
    [InlineData("assets/../../unsafe.dll")]
    [InlineData("/root/absolute.hex")]
    public void ProjectAssetsMustNotEscapeProjectRoot(string path)
    {
        Assert.Throws<InvalidDataException>(() =>
            NativeFlashProfileStore.InsideRoot(Path.GetTempPath(), path));
    }

    [Fact]
    public void DraftNeverAuthorizesUnknownProgrammingGeometry()
    {
        var draft = NativeFlashProfileDocument.CreateDraft("EPBA");
        Assert.Throws<FormatException>(() => draft.ValidateForFreeze());
        var plan = draft with
        {
            EcuModelCode = "HYDEPB", // one physically verified OEM method
            AppRequestCanId = 0x747,
            AppResponseCanId = 0x74F,
            BootRequestCanId = 0x18DA50F1,
            BootResponseCanId = 0x18DAF150,
            FlashStart = 0x10008000,
            FlashEndInclusive = 0x1006FFFF,
            FirmwareRelativePath = "assets/flash/firmware/firmware.hex",
            FirmwareSha256 = new string('A', 64),
            SecurityRelativePath = "assets/flash/security/safety.dll",
            SecuritySha256 = new string('B', 64)
        };
        plan.ValidateForFreeze();
        Assert.Equal((uint)0x18DA50F1, plan.GetEndpoint(true).Request);
        Assert.Equal("500K", plan.Get("BaudRate"));
        Assert.Equal("0x200", plan.Get("BlockLength"));
        Assert.Throws<NotSupportedException>(() =>
            (plan with { EcuModelCode = "EPBA" }).ValidateForFreeze());
        Assert.Equal("BootloadPhyID",
            plan.FingerprintEntries().First().Key);
        Assert.Throws<NotSupportedException>(() =>
            (plan with { FlashStart = 0x10000000 }).ValidateForFreeze());
        Assert.Throws<NotSupportedException>(() =>
            (plan with { EcuCanKbps = 125 }).ValidateForFreeze());
    }
    [Fact]
    public async Task HydEpbProjectAutoLoadsAllOriginalXmlFieldsWithoutExternalXml()
    {
        var root = Path.Combine(Path.GetTempPath(),
            "autotest-hydepb-reference-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var first = await NativeFlashProfileStore.EnsureHydEpbReferenceAsync(
                root, "HYDEPB");
            Assert.NotNull(first);
            var profile = first!.Profile;
            Assert.Equal((uint)0x747, profile.AppRequestCanId);
            Assert.Equal((uint)0x74F, profile.AppResponseCanId);
            Assert.Equal((uint)0x18DA50F1, profile.BootRequestCanId);
            Assert.Equal((uint)0x18DAF150, profile.BootResponseCanId);
            Assert.Equal((uint)0x10008000, profile.FlashStart);
            Assert.Equal((uint)0x1006FFFF, profile.FlashEndInclusive);
            Assert.Equal(500, profile.EcuCanKbps);
            Assert.Equal(512, profile.BlockLength);
            Assert.Equal("0x74F", profile.Get("VersionID"));
            Assert.Equal("0x18FE150E", profile.Get("DiagnosticsID"));
            Assert.Equal("73", profile.Get("BootFeature"));
            Assert.Equal("701", profile.Get("APPFeature"));
            Assert.Contains(profile.FingerprintEntries(),
                x => x.Key == "BootFeature" && x.Value == "73");
            Assert.Contains(profile.FingerprintEntries(),
                x => x.Key == "APPFeature" && x.Value == "701");
            var second = await NativeFlashProfileStore.EnsureHydEpbReferenceAsync(
                root, "HYDEPB");
            Assert.NotNull(second);
            Assert.Equal(first.Profile.ProfileId, second!.Profile.ProfileId);
            Assert.Equal(first.ProfileSha256, second.ProfileSha256);
            Assert.Null(profile.FirmwareRelativePath);
            Assert.Null(profile.SecurityRelativePath);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task ReferenceBackfillPreservesPinnedResourcesAndExplicitUserOverrides()
    {
        var root = Path.Combine(Path.GetTempPath(),
            "autotest-hydepb-merge-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var draft = NativeFlashProfileDocument.CreateDraft("HYDEPB") with
            {
                AppRequestCanId = 0x765,
                FirmwareRelativePath = "assets/flash/firmware/firmware.hex",
                FirmwareSha256 = new string('A', 64),
                SecurityRelativePath = "assets/flash/security/safety.dll",
                SecuritySha256 = new string('B', 64)
            };
            var saved = await NativeFlashProfileStore.SaveDraftAsync(root, draft, null);
            var merged = await NativeFlashProfileStore.EnsureHydEpbReferenceAsync(
                root, "HYDEPB");
            Assert.NotNull(merged);
            Assert.Equal(saved.Profile.ProfileId, merged!.Profile.ProfileId);
            Assert.Equal((uint)0x765, merged.Profile.AppRequestCanId);
            Assert.Equal((uint)0x74F, merged.Profile.AppResponseCanId);
            Assert.Equal(saved.Profile.FirmwareSha256, merged.Profile.FirmwareSha256);
            Assert.Equal(saved.Profile.SecuritySha256, merged.Profile.SecuritySha256);
            Assert.Throws<NotSupportedException>(() => merged.Profile.ValidateForFreeze());
        }
        finally { Directory.Delete(root, true); }
    }

}

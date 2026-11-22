using System.IO;
using AutoTest.FlashLab.Configuration;
using AutoTest.FlashLab.Programming;
using Xunit;

namespace AutoTest.Desktop.Tests.Projects;

public sealed class NativeFlashProfileTests
{
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
        Assert.Equal("BootloadPhyID",
            plan.FingerprintEntries().First().Key);
        Assert.Throws<NotSupportedException>(() =>
            (plan with { FlashStart = 0x10000000 }).ValidateForFreeze());
        Assert.Throws<NotSupportedException>(() =>
            (plan with { EcuCanKbps = 125 }).ValidateForFreeze());
    }
}

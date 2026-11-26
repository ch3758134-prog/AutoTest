using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using AutoTest.Desktop.Projects;
using Xunit;

namespace AutoTest.Desktop.Tests.Projects;

public sealed class ProductionCatalogContractTests
{
    [Fact]
    public async Task EmptyCatalogNeverGuessesAnEcuProject()
    {
        var root = TempDirectory();
        try
        {
            var catalog = new ProductionProjectCatalog(root);
            var result = await catalog.ResolveAsync("00001234");
            Assert.False(result.Accepted);
            Assert.Null(result.Package);
            Assert.Equal("SINGLE_NOT_UNIQUE", result.Code);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public async Task ProductAndSerialFieldsRequireAnExactInstalledBinding()
    {
        var root = TempDirectory();
        try
        {
            var catalog = new ProductionProjectCatalog(root);
            await catalog.SaveSettingsAsync(new(
                ProductionRoutingSettings.Fields, "|", 1, 2, 0));
            var missing = await catalog.ResolveAsync("EPB-A|00001234");
            Assert.False(missing.Accepted);
            Assert.Equal("PROJECT_NOT_MAPPED", missing.Code);
            var incomplete = await catalog.ResolveAsync("EPB-A");
            Assert.False(incomplete.Accepted);
            Assert.Equal("FIELDS_MISSING", incomplete.Code);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public async Task RegistryModeDoesNotInferProductFromSerial()
    {
        var root = TempDirectory();
        try
        {
            var catalog = new ProductionProjectCatalog(root);
            await catalog.SaveSettingsAsync(new(
                ProductionRoutingSettings.Registry, "|", 1, 2, 0));
            var unknown = await catalog.ResolveAsync("00001234");
            Assert.Equal("SERIAL_UNREGISTERED", unknown.Code);
            await catalog.SetRegistryIdentityAsync("00001234", "EPB-A");
            var registered = await catalog.ResolveAsync("00001234");
            Assert.Equal("PROJECT_NOT_MAPPED", registered.Code);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public async Task RoutePolicyRejectsAmbiguousColumnPositions()
    {
        var root = TempDirectory();
        try
        {
            var catalog = new ProductionProjectCatalog(root);
            await Assert.ThrowsAsync<InvalidDataException>(async () =>
                await catalog.SaveSettingsAsync(new(
                    ProductionRoutingSettings.Fields, "|", 1, 1, 0)));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public async Task PackageRejectsDirectoryTraversalWithoutExtractingFiles()
    {
        var root = TempDirectory();
        try
        {
            var archive = Path.Combine(root, "traversal.atpkg");
            var content = new byte[] { 1, 2, 3 };
            var file = new ProductionPackageFile("../escape.bin",
                Convert.ToHexString(SHA256.HashData(content)), content.Length);
            var header = new ProductionPackageManifest(1, Guid.NewGuid(), 1,
                "Invalid", new string('A', 64), new string('B', 64),
                [new ProductionPackageFile("project.autotest-project.json",
                    new string('A', 64), 1), file]);
            using (var zip = ZipFile.Open(archive, ZipArchiveMode.Create))
            {
                using (var output = zip.CreateEntry(
                    ProductionProjectPackageService.MetadataEntry).Open())
                    JsonSerializer.Serialize(output, header);
                using (var output = zip.CreateEntry("../escape.bin").Open())
                    output.Write(content);
            }
            await Assert.ThrowsAsync<InvalidDataException>(() =>
                ProductionProjectPackageService.ValidateArchiveAsync(archive));
            Assert.False(File.Exists(Path.Combine(root, "escape.bin")));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public async Task RepeatedSerialCannotSilentlyChangeProductOwnership()
    {
        var root = TempDirectory();
        try
        {
            var catalog = new ProductionProjectCatalog(root);
            await catalog.SetRegistryIdentityAsync("S00001", "EPB-A");
            await catalog.SetRegistryIdentityAsync("S00001", "EPB-A");
            await Assert.ThrowsAsync<InvalidDataException>(async () =>
                await catalog.SetRegistryIdentityAsync("S00001", "EPB-B"));
            await catalog.SaveSettingsAsync(new(
                ProductionRoutingSettings.Registry, "|", 1, 2, 0));
            var decision = await catalog.ResolveAsync("S00001");
            Assert.Equal("PROJECT_NOT_MAPPED", decision.Code);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    private static string TempDirectory()
    {
        var dir = Path.Combine(Path.GetTempPath(),
            "autotest-catalog-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }
}

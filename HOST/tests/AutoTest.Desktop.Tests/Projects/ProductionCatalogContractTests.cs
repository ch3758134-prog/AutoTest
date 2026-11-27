using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using AutoTest.Application.Production;
using AutoTest.Desktop.Projects;
using Xunit;

namespace AutoTest.Desktop.Tests.Projects;

public sealed class ProductionCatalogContractTests
{
    [Fact]
    public async Task EmptyCatalogCannotChooseAProgrammingProject()
    {
        var root = TempDirectory();
        try
        {
            var catalog = new ProductionProjectCatalog(root);
            var result = await catalog.ResolveAsync("HYDEP-00012345");
            Assert.False(result.Accepted);
            Assert.Null(result.Package);
            Assert.Equal("MARKER_UNBOUND", result.Code);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Theory]
    [InlineData("HYDEP")]
    [InlineData("HYDEP-")]
    [InlineData("-00001")]
    [InlineData("HYDEP-00001\n")]
    [InlineData("EPB?1-00001")]
    [InlineData(" HYDEP-00001")]
    [InlineData("HYDEP-000 01")]
    public async Task InvalidBarcodeNeverChoosesAnInstalledProject(string raw)
    {
        var root = TempDirectory();
        try
        {
            var catalog = new ProductionProjectCatalog(root);
            var result = await catalog.ResolveAsync(raw);
            Assert.False(result.Accepted);
            Assert.Equal("BARCODE_INVALID", result.Code);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public async Task BindingRejectsPackagesThatWereNotInstalled()
    {
        var root = TempDirectory();
        try
        {
            var catalog = new ProductionProjectCatalog(root);
            await Assert.ThrowsAsync<InvalidDataException>(() =>
                catalog.BindMarkerAsync("HYDEP", new string('A', 64)));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void LabelMarkerMayNotIncludeSeparatorOrWhitespace()
    {
        Assert.Equal("HYDEP", FixedMarkerBarcode.NormalizeMarker("hydep"));
        Assert.Throws<InvalidDataException>(() =>
            FixedMarkerBarcode.NormalizeMarker("EPB-A"));
        Assert.Throws<InvalidDataException>(() =>
            FixedMarkerBarcode.NormalizeMarker(" HYDEP"));
    }

    [Fact]
    public async Task ArchiveRejectsDirectoryTraversalBeforeExtraction()
    {
        var root = TempDirectory();
        try
        {
            var archive = Path.Combine(root, "invalid.atpkg");
            var bytes = new byte[] { 1, 2, 3 };
            var file = new ProductionPackageFile("../outside.bin",
                Convert.ToHexString(SHA256.HashData(bytes)), bytes.Length);
            var header = new ProductionPackageManifest(1, Guid.NewGuid(), 1,
                "Invalid", new string('A', 64), new string('B', 64),
                [new ProductionPackageFile("project.autotest-project.json",
                    new string('A', 64), 1), file]);
            using (var zip = ZipFile.Open(archive, ZipArchiveMode.Create))
            {
                using (var output = zip.CreateEntry(
                    ProductionProjectPackageService.MetadataEntry).Open())
                    JsonSerializer.Serialize(output, header);
                using (var output = zip.CreateEntry("../outside.bin").Open())
                    output.Write(bytes);
            }
            await Assert.ThrowsAsync<InvalidDataException>(() =>
                ProductionProjectPackageService.ValidateArchiveAsync(archive));
            Assert.False(File.Exists(Path.Combine(root, "outside.bin")));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    private static string TempDirectory()
    {
        var root = Path.Combine(Path.GetTempPath(),
            "autotest-marker-contract-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }
}
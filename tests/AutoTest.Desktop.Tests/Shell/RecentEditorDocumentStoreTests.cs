using System.IO;
using AutoTest.Desktop.Shell;
using Xunit;

namespace AutoTest.Desktop.Tests.Shell;

public sealed class RecentEditorDocumentStoreTests
{
    [Fact]
    public void RemembersOnlyRealResourceKeysPerProjectAndDoesNotCreateProjectAssets()
    {
        var root = Path.Combine(Path.GetTempPath(),
            "autotest-ui-lastdoc-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var preference = Path.Combine(root, "user-settings.json");
            var first = Path.Combine(root, "A", "project.autotest-project.json");
            var second = Path.Combine(root, "B", "project.autotest-project.json");
            var a = "test|" + Guid.NewGuid();
            var b = "project|" + Guid.NewGuid();
            var store = new RecentEditorDocumentStore(preference);
            Assert.Null(store.Read(first));
            store.Remember(first, a);
            store.Remember(second, b);
            store.Remember(first, "home");
            store.Remember(first, "config-overview");
            store.Remember(first, "test|not-a-guid");
            var reread = new RecentEditorDocumentStore(preference);
            Assert.Equal(a, reread.Read(first));
            Assert.Equal(b, reread.Read(second));
            Assert.False(File.Exists(first));
            Assert.False(File.Exists(second));
            Assert.True(File.Exists(preference));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void LatestRealEditorWinsWithoutTouchingOtherProjects()
    {
        var root = Path.Combine(Path.GetTempPath(),
            "autotest-ui-restore-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var store = new RecentEditorDocumentStore(Path.Combine(root, "preference.json"));
            var projectA = Path.Combine(root, "A.json");
            var projectB = Path.Combine(root, "B.json");
            var previous = "dbc|" + Guid.NewGuid();
            var newest = "test|" + Guid.NewGuid();
            store.Remember(projectA, previous);
            store.Remember(projectB, "project|" + Guid.NewGuid());
            store.Remember(projectA, newest);
            Assert.Equal(newest, store.Read(projectA));
            Assert.StartsWith("project|", store.Read(projectB));
        }
        finally { Directory.Delete(root, recursive: true); }
    }
}

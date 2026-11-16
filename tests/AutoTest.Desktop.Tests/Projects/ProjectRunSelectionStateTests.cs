using AutoTest.Desktop.Projects;
using Xunit;

namespace AutoTest.Desktop.Tests.Projects;

public sealed class ProjectRunSelectionStateTests
{
    [Fact]
    public void ProjectAndEditorShareOneTransientEntrySelection_WithoutChangingDefaults()
    {
        var model = new ProjectRunSelectionState();
        var projectId = Guid.NewGuid();
        var itemId = Guid.NewGuid();
        var changes = new List<Guid>();
        model.Changed += changes.Add;

        var navigator = model.Get(projectId, itemId, enabled: true);
        var editor = model.Get(projectId, itemId, enabled: false);
        Assert.Same(navigator, editor);
        Assert.True(editor.IsSelected);

        navigator.IsSelected = false;
        Assert.False(editor.IsSelected);
        Assert.Equal(new[] { projectId }, changes);
        Assert.Same(editor, model.Get(projectId, itemId, enabled: true));
        Assert.False(model.Get(projectId, itemId, enabled: true).IsSelected);

        // The same formal package entry could be referenced in another test
        // project. Temporary choices must never leak between project identities.
        var unrelated = model.Get(Guid.NewGuid(), itemId, enabled: true);
        Assert.True(unrelated.IsSelected);
    }

    [Fact]
    public void UnselectedEntryDefaultInitializesAsOff_AndDoesNotRaiseAnEventOnRead()
    {
        var model = new ProjectRunSelectionState();
        var events = 0;
        model.Changed += _ => events++;
        var item = model.Get(Guid.NewGuid(), Guid.NewGuid(), enabled: false);
        Assert.False(item.IsSelected);
        Assert.Equal(0, events);
        item.IsSelected = false;
        Assert.Equal(0, events);
        item.IsSelected = true;
        Assert.Equal(1, events);
    }
}

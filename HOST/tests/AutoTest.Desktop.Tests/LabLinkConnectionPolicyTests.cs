using AutoTest.LabLinkGui.LabLink;
using Xunit;

namespace AutoTest.Desktop.Tests;

public class LabLinkConnectionPolicyTests
{
    private static LabBoardIdentity Board(string path) => new("主控板", path, "VID_1234&PID_5678", null);

    [Fact] public void ZeroMatchingBoardsDoesNotConnect() =>
        Assert.Equal(LabConnectionDecision.None, LabLinkConnectionPolicy.Decide(System.Array.Empty<LabBoardIdentity>()));

    [Fact] public void ExactlyOneMatchingBoardAutoConnects() =>
        Assert.Equal(LabConnectionDecision.AutoConnect, LabLinkConnectionPolicy.Decide(new[] { Board("A") }));

    [Fact] public void MultipleMatchingBoardsRequireExplicitChoice() =>
        Assert.Equal(LabConnectionDecision.RequireSelection, LabLinkConnectionPolicy.Decide(new[] { Board("A"), Board("B") }));

    [Fact] public void ChoiceLabelDistinguishesBoardsWithSameDisplayName()
    {
        Assert.NotEqual(Board("path-a").ChoiceLabel, Board("path-b").ChoiceLabel);
    }
}



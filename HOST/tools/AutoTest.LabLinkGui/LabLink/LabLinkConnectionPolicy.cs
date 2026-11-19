using System;
using System.Collections.Generic;

namespace AutoTest.LabLinkGui.LabLink;

/// <summary>Pure UI decision logic. The actual matching/discovery belongs to DEVICE.</summary>
public static class LabLinkConnectionPolicy
{
    public static LabConnectionDecision Decide(IReadOnlyList<LabBoardIdentity> matchingBoards)
    {
        ArgumentNullException.ThrowIfNull(matchingBoards);
        if (matchingBoards.Count == 0) return LabConnectionDecision.None;
        if (matchingBoards.Count == 1) return LabConnectionDecision.AutoConnect;
        return LabConnectionDecision.RequireSelection;
    }
}

public enum LabConnectionDecision { None, AutoConnect, RequireSelection }


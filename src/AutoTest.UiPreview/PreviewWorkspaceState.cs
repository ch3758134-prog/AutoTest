namespace AutoTest.UiPreview;

/// <summary>Pure UI dock state, preserving explicit user choices across viewport resizes.</summary>
public sealed class PreviewWorkspaceState
{
    public bool InspectorVisible { get; private set; } = true;
    public bool BottomVisible { get; private set; } = true;
    public bool InspectorOverride { get; private set; }
    public bool BottomOverride { get; private set; }

    public void Adapt(double width, double height)
    {
        if (!InspectorOverride) InspectorVisible = width >= 1120;
        if (!BottomOverride) BottomVisible = height >= 640;
    }

    public void ToggleInspector()
    {
        InspectorOverride = true;
        InspectorVisible = !InspectorVisible;
    }

    public void ToggleBottom()
    {
        BottomOverride = true;
        BottomVisible = !BottomVisible;
    }
}

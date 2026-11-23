using System.Windows.Controls;

namespace AutoTest.Desktop.Projects;

public partial class InspectionStationProcedureEditorView : UserControl
{
    public InspectionStationProcedureEditorView(InspectionStationProcedureEditorViewModel model)
    {
        InitializeComponent();
        DataContext = model;
    }
}

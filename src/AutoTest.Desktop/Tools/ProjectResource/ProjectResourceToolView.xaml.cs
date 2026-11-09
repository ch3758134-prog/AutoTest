using System.Windows.Controls;

namespace AutoTest.Desktop.Tools.ProjectResource;

public partial class ProjectResourceToolView : UserControl
{
    public ProjectResourceToolView()
    {
        InitializeComponent();
        DataContext = new ProjectResourceToolViewModel();
    }
}

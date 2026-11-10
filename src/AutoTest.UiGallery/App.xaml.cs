using System.Windows;

namespace AutoTest.UiGallery;

/// <summary>
/// Isolated WPF visual validation process; it does not import the old Prism Shell
/// and cannot create a physical device or a simulated ECU success.
/// </summary>
public partial class App : Application { }

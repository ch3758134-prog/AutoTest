using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;

namespace AutoTest.Desktop.Shell.R1;

public partial class R1WorkspaceView : UserControl
{
    private readonly R1WorkspaceViewModel _viewModel;

    public R1WorkspaceView(R1WorkspaceViewModel viewModel)
    {
        _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        InitializeComponent();
        DataContext = _viewModel;
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        ApplyVisualSettings();
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        ApplyVisualSettings();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e) =>
        _viewModel.PropertyChanged -= OnViewModelPropertyChanged;

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(R1WorkspaceViewModel.Theme) or nameof(R1WorkspaceViewModel.Density))
            ApplyVisualSettings();
    }

    private void ApplyVisualSettings()
    {
        var palette = _viewModel.Theme == R1Theme.Light ? "Light" : "Dark";
        // Only the first merged dictionary is changed; controls and main app resources stay untouched.
        Resources.MergedDictionaries[0].Source = new Uri(
            $"/AutoTest.Desktop;component/Shell/R1/Themes/{palette}.xaml",
            UriKind.Relative);
        FontSize = _viewModel.Density == R1Density.Compact ? 13 : 14;
        Resources["R1ControlHeight"] = _viewModel.Density == R1Density.Compact ? 30d : 38d;
    }
}

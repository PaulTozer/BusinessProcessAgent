using BusinessProcessAgent.Core.Models;

namespace BusinessProcessAgent.App.Views;

public sealed partial class ProcessViewerPage : Page
{
    private IReadOnlyList<BusinessProcess> _processes = [];

    public ProcessViewerPage()
    {
        this.InitializeComponent();
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        var mgr = App.TrayManager;
        if (mgr is null) return;

        _processes = await mgr.Coordinator.GetRecentProcessesAsync(200);

        // Wrap in a view-model-like projection for binding
        processList.ItemsSource = _processes.Select(p => new
        {
            p.Name,
            StepCount = p.Steps.Count,
            Steps = p.Steps,
        }).ToList();
    }

    private void OnProcessSelected(object sender, SelectionChangedEventArgs e)
    {
        if (processList.SelectedItem is null) return;

        // Use reflection-free approach: find the matching process by index
        var idx = processList.SelectedIndex;
        if (idx >= 0 && idx < _processes.Count)
        {
            stepList.ItemsSource = _processes[idx].Steps;
        }
    }

    private void OnBack(object sender, RoutedEventArgs e)
    {
        if (Frame.CanGoBack)
            Frame.GoBack();
    }
}

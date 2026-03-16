using BusinessProcessAgent.App.Tray;
using BusinessProcessAgent.Core.Models;
using System.Collections.ObjectModel;

namespace BusinessProcessAgent.App.Views
{
    public partial class MainPage : Page
    {
        private readonly ObservableCollection<ProcessStep> _activityItems = new();

        public MainPage()
        {
            this.InitializeComponent();
            activityFeed.ItemsSource = _activityItems;

            var mgr = App.TrayManager;
            if (mgr is not null)
            {
                mgr.Coordinator.StepRecorded += OnStepRecorded;
            }
        }

        private void OnStepRecorded(ProcessStep step)
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                _activityItems.Insert(0, step);
                if (_activityItems.Count > 100)
                    _activityItems.RemoveAt(_activityItems.Count - 1);

                statusBar.Severity = InfoBarSeverity.Success;
                statusBar.Title = "Observing";
                statusBar.Message = $"Step #{step.StepNumber}: {step.HighLevelAction}";
            });
        }

        private async void OnObserveToggle(object sender, RoutedEventArgs e)
        {
            var mgr = App.TrayManager;
            if (mgr is null) return;

            await mgr.ToggleObservationAsync();

            if (mgr.Coordinator.IsObserving)
            {
                btnObserve.Content = "Stop Observing";
                statusBar.Severity = InfoBarSeverity.Success;
                statusBar.Title = "Observing";
                statusBar.Message = "Capturing and analyzing your workflow...";
            }
            else
            {
                btnObserve.Content = "Start Observing";
                statusBar.Severity = InfoBarSeverity.Informational;
                statusBar.Title = "Paused";
                statusBar.Message = "Observation paused.";
            }
        }

        private void OnViewProcesses(object sender, RoutedEventArgs e)
        {
            Frame.Navigate(typeof(ProcessViewerPage));
        }

        private void OnSettings(object sender, RoutedEventArgs e)
        {
            Frame.Navigate(typeof(SettingsPage));
        }
    }
}

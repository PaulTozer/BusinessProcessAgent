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
                btnDiscard.IsEnabled = true;
                statusBar.Severity = InfoBarSeverity.Success;
                statusBar.Title = "Observing";
                statusBar.Message = "Capturing and analyzing your workflow...";
            }
            else
            {
                btnObserve.Content = "Start Observing";
                btnDiscard.IsEnabled = false;
                statusBar.Severity = InfoBarSeverity.Informational;
                statusBar.Title = "Paused";
                statusBar.Message = "Observation paused.";
            }
        }

        private async void OnDiscardRestart(object sender, RoutedEventArgs e)
        {
            var dialog = new ContentDialog
            {
                Title = "Discard Current Session?",
                Content = "This will delete all steps recorded in the current session and start fresh. This cannot be undone.",
                PrimaryButtonText = "Discard & Restart",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Close,
                XamlRoot = this.XamlRoot,
            };

            if (await dialog.ShowAsync() == ContentDialogResult.Primary)
            {
                var mgr = App.TrayManager;
                if (mgr is null) return;

                await mgr.Coordinator.DiscardCurrentAndRestartAsync();
                _activityItems.Clear();

                statusBar.Severity = InfoBarSeverity.Success;
                statusBar.Title = "Restarted";
                statusBar.Message = "Previous session discarded. Fresh observation started.";
            }
        }

        private async void OnClearAll(object sender, RoutedEventArgs e)
        {
            var dialog = new ContentDialog
            {
                Title = "Clear All Data?",
                Content = "This will permanently delete ALL recorded sessions, process steps, and screenshots. This cannot be undone.",
                PrimaryButtonText = "Clear Everything",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Close,
                XamlRoot = this.XamlRoot,
            };

            if (await dialog.ShowAsync() == ContentDialogResult.Primary)
            {
                var mgr = App.TrayManager;
                if (mgr is null) return;

                await mgr.Coordinator.ClearAllDataAsync();
                _activityItems.Clear();

                btnObserve.Content = "Start Observing";
                btnObserve.IsChecked = false;
                btnDiscard.IsEnabled = false;

                statusBar.Severity = InfoBarSeverity.Informational;
                statusBar.Title = "Data Cleared";
                statusBar.Message = "All sessions and screenshots have been deleted.";
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

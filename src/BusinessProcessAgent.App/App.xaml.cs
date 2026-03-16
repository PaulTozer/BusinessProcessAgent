using BusinessProcessAgent.App.Tray;
using BusinessProcessAgent.Core.Compliance;
using BusinessProcessAgent.Core.Configuration;
using BusinessProcessAgent.Core.Services;
using BusinessProcessAgent.Core.Storage;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml.Navigation;

namespace BusinessProcessAgent.App
{
    public partial class App : Application
    {
        private Window window = Window.Current;
        private TrayIconManager? _trayManager;

        internal static TrayIconManager? TrayManager { get; private set; }

        public App()
        {
            this.InitializeComponent();
        }

        protected override void OnLaunched(LaunchActivatedEventArgs e)
        {
            // ── Load settings ──
            var appDir = AppContext.BaseDirectory;
            var dataDir = Path.Combine(appDir, "data");
            Directory.CreateDirectory(dataDir);

            var settingsPath = Path.Combine(dataDir, "settings.json");
            var settings = AppSettings.Load(settingsPath);

            // ── Create logger ──
            using var loggerFactory = LoggerFactory.Create(builder =>
            {
                builder.AddDebug();
                builder.SetMinimumLevel(LogLevel.Debug);
            });

            // ── Compliance services ──
            var redaction = new RedactionService(loggerFactory.CreateLogger<RedactionService>());
            redaction.Configure(settings.Compliance);

            var encryption = new EncryptionService(loggerFactory.CreateLogger<EncryptionService>());
            encryption.Configure(settings.Compliance);

            var audit = new AuditLogger(loggerFactory.CreateLogger<AuditLogger>());
            audit.Configure(settings.Compliance, Path.Combine(dataDir, "audit"));

            // ── Core services ──
            var screenState = new ScreenStateMonitor();
            var capture = new ScreenCaptureService(loggerFactory.CreateLogger<ScreenCaptureService>());

            var poller = new ForegroundWindowPoller(
                TimeSpan.FromSeconds(settings.Observation.PollingIntervalSeconds),
                screenState,
                loggerFactory.CreateLogger<ForegroundWindowPoller>());

            var analysisService = new ProcessAnalysisService(
                loggerFactory.CreateLogger<ProcessAnalysisService>());
            if (settings.AzureAi.Enabled)
                analysisService.Configure(settings.AzureAi);

            var assembler = new ProcessAssembler(loggerFactory.CreateLogger<ProcessAssembler>());

            var dbPath = Path.Combine(dataDir, "processes.db");
            var store = new ProcessStore(dbPath, loggerFactory.CreateLogger<ProcessStore>());

            var coordinator = new ObservationCoordinator(
                poller, capture, analysisService, assembler, store,
                redaction, encryption, audit,
                loggerFactory.CreateLogger<ObservationCoordinator>());
            coordinator.Configure(settings.Observation, settings.Compliance);

            // ── Tray icon ──
            _trayManager = new TrayIconManager(
                coordinator, analysisService, redaction, encryption, audit,
                settings, Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread(),
                loggerFactory.CreateLogger<TrayIconManager>());
            TrayManager = _trayManager;

            // ── Main window ──
            window ??= new Window();
            window.Title = "Business Process Agent";

            if (window.Content is not Frame rootFrame)
            {
                rootFrame = new Frame();
                rootFrame.NavigationFailed += OnNavigationFailed;
                window.Content = rootFrame;
            }

            _ = rootFrame.Navigate(typeof(Views.MainPage), e.Arguments);
            window.Activate();
        }

        void OnNavigationFailed(object sender, NavigationFailedEventArgs e)
        {
            throw new Exception("Failed to load Page " + e.SourcePageType.FullName);
        }
    }
}

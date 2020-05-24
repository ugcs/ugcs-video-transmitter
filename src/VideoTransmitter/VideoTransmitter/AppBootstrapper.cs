using Caliburn.Micro;
using Ninject;
using System;
using System.Collections.Generic;
using System.Windows;
using SSDPDiscoveryService;
using VideoTransmitter;
using VideoTransmitter.Properties;
using VideoTransmitter.ViewModels;
using UcsService;
using VideoSources;
using VideoTransmitter.Log;
using System.Text.RegularExpressions;

namespace VideoTransmitter
{
    public class AppBootstrapper : BootstrapperBase
    {

        private ILogger logger = new Logger(typeof(AppBootstrapper));
        private bool _isInitialized;

        public IKernel Kernel
        {
            get
            {
                return App.Kernel;
            }
            set
            {
                App.Kernel = value;
            }
        }
        public AppBootstrapper()
        {
            logger.LogInfoMessage("Application started");
            Initialize();
            logger.LogInfoMessage("Application initialized");
        }

        protected override void Configure()
        {
            Kernel = new StandardKernel();
            Kernel.Bind<IDiscoveryService>().To<DiscoveryService>().InSingletonScope();
            Kernel.Bind<ConnectionService>().To<ConnectionService>().InSingletonScope();
            Kernel.Bind<IWindowManager>().To<WindowManager>().InSingletonScope();
            Kernel.Bind<IEventAggregator>().To<EventAggregator>().InSingletonScope();
            Kernel.Bind<MainViewModel>().ToSelf().InSingletonScope();
            Kernel.Bind<VehicleListener>().ToSelf().InSingletonScope();
            Kernel.Bind<TelemetryListener>().ToSelf().InSingletonScope();
            Kernel.Bind<VehicleService>().ToSelf().InSingletonScope();
            Kernel.Bind<VideoSourcesService>().ToSelf().InSingletonScope();

            _isInitialized = true;
        }

        private string generteInstallationId()
        {
            return Regex.Replace(Convert.ToBase64String(Guid.NewGuid().ToByteArray()), "[/+=]", "");
        }

        protected override void OnStartup(object sender, StartupEventArgs e)
        {
            if (String.IsNullOrWhiteSpace(Settings.Default.InstallationId))
            {
                Settings.Default.InstallationId = generteInstallationId();
                Settings.Default.Save();
            }

            if (_isInitialized)
            {
                DisplayRootViewFor<MainViewModel>();
            }
        }
        protected override void OnExit(object sender, EventArgs e)
        {
            Settings.Default.Save();
            Kernel.Dispose();
            base.OnExit(sender, e);
            Environment.Exit(0);
        }

        protected override object GetInstance(Type service, string key)
        {
            if (service == null)
                throw new ArgumentNullException("service");

            return Kernel.Get(service);
        }

        protected override IEnumerable<object> GetAllInstances(Type service)
        {
            return Kernel.GetAll(service);
        }

        protected override void BuildUp(object instance)
        {
            Kernel.Inject(instance);
        }
    }
}

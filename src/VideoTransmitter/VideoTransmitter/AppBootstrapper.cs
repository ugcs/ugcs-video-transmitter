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
using System.Text.RegularExpressions;
using log4net;
using VideoTransmitter.Models;

namespace VideoTransmitter
{
    public class AppBootstrapper : BootstrapperBase
    {

        private log4net.ILog logger = log4net.LogManager.GetLogger(typeof(AppBootstrapper));
        private bool _isInitialized;
        private AppSettingsObserver _appSettingsObserver;
        

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
            log4net.Config.XmlConfigurator.Configure();
            GlobalContext.Properties["host"] = Environment.MachineName;
            logger.Info("Application started");
            Initialize();
            logger.Info("Application initialized");
        }

        protected override void Configure()
        {
            var k = new StandardKernel();
            k.Bind<IDiscoveryService>().To<DiscoveryService>().InSingletonScope();
            k.Bind<ConnectionService>().To<ConnectionService>().InSingletonScope();
            k.Bind<IWindowManager>().To<WindowManager>().InSingletonScope();
            k.Bind<IEventAggregator>().To<EventAggregator>().InSingletonScope();
            k.Bind<MainViewModel>().ToSelf().InSingletonScope();
            k.Bind<VehicleListener>().ToSelf().InSingletonScope();
            k.Bind<TelemetryListener>().ToSelf().InSingletonScope();
            k.Bind<VehicleService>().ToSelf().InSingletonScope();
            k.Bind<VideoSourcesService>().ToSelf().InSingletonScope();
            Kernel = k;

            _appSettingsObserver = new AppSettingsObserver(k, Settings.Default);

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

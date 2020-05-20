using Ninject;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using VideoTransmitter.Properties;

namespace VideoTransmitter
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {

        public static IKernel Kernel { get; set; }
        public App()
        {
            InitializeComponent();
            ShutdownMode = ShutdownMode.OnLastWindowClose;

            CultureInfo customCulture = new CultureInfo(Settings.Default.CurrentUICulture);
            customCulture.NumberFormat.NumberDecimalSeparator = ".";
            Thread.CurrentThread.CurrentUICulture = customCulture;
        }
    }
}
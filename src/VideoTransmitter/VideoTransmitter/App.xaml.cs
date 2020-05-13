using Ninject;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;

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
        }
    }
}
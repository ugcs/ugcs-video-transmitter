using System.ComponentModel;
using System.Windows;

namespace VideoTransmitter.Views
{
    /// <summary>
    /// Interaction logic for MainView.xaml
    /// </summary>
    public partial class MainView : Window
    {
        public MainView()
        {
            InitializeComponent();
        }

        private void Media_MediaOpening(object sender, Unosquare.FFME.Common.MediaOpeningEventArgs e)
        {
            e.Options.MinimumPlaybackBufferPercent = 0;
        }


        protected override void OnClosing(CancelEventArgs e)
        {
            MessageBoxResult result = MessageBox.Show(Properties.Resources.Doyouwanttocloseapplication, Properties.Resources.Shutdownconfirmation, MessageBoxButton.OKCancel, MessageBoxImage.Question);
            if (result == MessageBoxResult.OK)
            {
                e.Cancel = false;
            }
            else
            {
                e.Cancel = true;
            }
        }

    }
}

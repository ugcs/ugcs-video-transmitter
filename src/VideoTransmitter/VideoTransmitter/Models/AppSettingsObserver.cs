using Ninject;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using VideoTransmitter.Properties;
using VideoTransmitter.ViewModels;

namespace VideoTransmitter.Models
{
    internal sealed class AppSettingsObserver
    {
        private const string CUSTOM_VS_NAME = "#custom_video_source";
        private Settings _settings;
        private StandardKernel _kernel;

        public AppSettingsObserver(StandardKernel k, Settings s)
        {
            _kernel = k ?? throw new ArgumentNullException(nameof(k));

            _settings = s ?? throw new ArgumentNullException(nameof(s));
            s.SettingChanging += onSettingChanging;

            setCustomVideoServiceUri(s.CustomVideoSourceUri);
        }


        private void onSettingChanging(object sender, System.Configuration.SettingChangingEventArgs e)
        {
            if (e.SettingName == nameof(Settings.CustomVideoSourceUri))
                onCustomVideoSourceChanging(_settings.CustomVideoSourceUri, (string)e.NewValue);

        }

        private void onCustomVideoSourceChanging(string oldValue, string newValue)
        {
            setCustomVideoServiceUri(newValue);
        }

        private void setCustomVideoServiceUri(string uri)
        {
            Uri newUri = null;
            if (!String.IsNullOrEmpty(uri))
                newUri = new Uri(uri);

            var mainVm = _kernel.Get<MainViewModel>();
            mainVm.CustomVideoSourceUri = newUri;
        }
    }
}

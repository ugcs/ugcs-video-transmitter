using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using VideoTransmitter.Properties;

namespace VideoTransmitter.ViewModels
{
    public class SettingsViewModel : Caliburn.Micro.PropertyChangedBase
    {
        private Action onSave;
        public SettingsViewModel(Action onSave)
        {
            this.onSave = onSave;
            TailNumber = Settings.Default.TailNumber;

            UgcsAutomatic = Settings.Default.UgcsAutomatic;
            UgcsDirectConnection = Settings.Default.UgcsDirectConnection;
            UcgsAddress = Settings.Default.UcgsAddress;
            UcgsPort = Settings.Default.UcgsPort;

            VideoServerAutomatic = Settings.Default.VideoServerAutomatic;
            VideoServerDirectConnection = Settings.Default.VideoServerDirectConnection;
            VideoServerAddress = Settings.Default.VideoServerAddress;
            VideoServerPort = Settings.Default.VideoServerPort;
        }        

        public string _tailNumber;
        public string TailNumber
        {
            get
            {
                return _tailNumber;
            }
            set
            {
                _tailNumber = value;
                NotifyOfPropertyChange(() => TailNumber);
            }
        }

        public bool _ugcsAutomatic;
        public bool UgcsAutomatic
        {
            get
            {
                return _ugcsAutomatic;
            }
            set
            {
                _ugcsAutomatic = value;
                NotifyOfPropertyChange(() => UgcsAutomatic);
            }
        }

        public bool _ugcsDirectConnection;
        public bool UgcsDirectConnection
        {
            get
            {
                return _ugcsDirectConnection;
            }
            set
            {
                _ugcsDirectConnection = value;
                NotifyOfPropertyChange(() => UgcsDirectConnection);
            }
        }

        public string _ugcsAddress;
        public string UcgsAddress
        {
            get
            {
                return _ugcsAddress;
            }
            set
            {
                _ugcsAddress = value;
                NotifyOfPropertyChange(() => UcgsAddress);
            }
        }

        public int _ugcsPort;
        public int UcgsPort
        {
            get
            {
                return _ugcsPort;
            }
            set
            {
                _ugcsPort = value;
                NotifyOfPropertyChange(() => UcgsPort);
            }
        }

        private bool _videoServerAutomatic;
        public bool VideoServerAutomatic
        {
            get
            {
                return _videoServerAutomatic;
            }
            set
            {
                _videoServerAutomatic = value;
                NotifyOfPropertyChange(() => VideoServerAutomatic);
            }
        }

        public bool _videoServerDirectConnection;
        public bool VideoServerDirectConnection
        {
            get
            {
                return _videoServerDirectConnection;
            }
            set
            {
                _videoServerDirectConnection = value;
                NotifyOfPropertyChange(() => VideoServerDirectConnection);
            }
        }

        public string _videoServerAddress;
        public string VideoServerAddress
        {
            get
            {
                return _videoServerAddress;
            }
            set
            {
                _videoServerAddress = value;
                NotifyOfPropertyChange(() => VideoServerAddress);
            }
        }

        public int _videoServerPort;
        public int VideoServerPort
        {
            get
            {
                return _videoServerPort;
            }
            set
            {
                _videoServerPort = value;
                NotifyOfPropertyChange(() => VideoServerPort);
            }
        }

        public void SaveSettings(Window wnd)
        {
            Settings.Default.TailNumber = TailNumber;

            Settings.Default.UgcsAutomatic = UgcsAutomatic;
            Settings.Default.UgcsDirectConnection = UgcsDirectConnection;
            Settings.Default.UcgsAddress = UcgsAddress;
            Settings.Default.UcgsPort = UcgsPort;

            Settings.Default.VideoServerAutomatic = VideoServerAutomatic;
            Settings.Default.VideoServerDirectConnection = VideoServerDirectConnection;
            Settings.Default.VideoServerAddress = VideoServerAddress;
            Settings.Default.VideoServerPort = VideoServerPort;

            Settings.Default.Save();
            onSave?.Invoke();
            wnd.Close();
        }
    }
}

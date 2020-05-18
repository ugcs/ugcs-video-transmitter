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
        private Action<HashSet<string>> onSave;
        public SettingsViewModel(Action<HashSet<string>> onSave)
        {
            this.onSave = onSave;
            TailNumber = Settings.Default.TailNumber;

            UgcsAutomatic = Settings.Default.UgcsAutomatic;
            UgcsDirectConnection = !Settings.Default.UgcsAutomatic;
            UcgsAddress = Settings.Default.UcgsAddress;
            UcgsPort = Settings.Default.UcgsPort;

            VideoServerAutomatic = Settings.Default.VideoServerAutomatic;
            VideoServerDirectConnection = !Settings.Default.VideoServerAutomatic;
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
            HashSet<string> changed = new HashSet<string>();
            if (TailNumber != Settings.Default.TailNumber)
            {
                Settings.Default.TailNumber = TailNumber;
                changed.Add("TailNumber");
            }
            if (UgcsAutomatic != Settings.Default.UgcsAutomatic)
            {
                Settings.Default.UgcsAutomatic = UgcsAutomatic;
                changed.Add("UgcsAutomatic");
            }
            if (UcgsAddress != Settings.Default.UcgsAddress || UcgsPort != Settings.Default.UcgsPort)
            {
                Settings.Default.UcgsAddress = UcgsAddress;
                Settings.Default.UcgsPort = UcgsPort;
                changed.Add("UcgsAddress");
            }
            if (VideoServerAutomatic != Settings.Default.VideoServerAutomatic)
            {
                Settings.Default.VideoServerAutomatic = VideoServerAutomatic;
                changed.Add("VideoServerAutomatic");
            }
            if (VideoServerAddress != Settings.Default.VideoServerAddress || UcgsPort != Settings.Default.VideoServerPort)
            {
                Settings.Default.VideoServerAddress = VideoServerAddress;
                Settings.Default.VideoServerPort = VideoServerPort;
                changed.Add("VideoServerAddress");
            }

            Settings.Default.Save();
            onSave?.Invoke(changed);
            wnd.Close();
        }
    }
}

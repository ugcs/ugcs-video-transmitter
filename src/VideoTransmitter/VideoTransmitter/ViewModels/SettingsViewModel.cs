using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
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

            UcgsAddress = Settings.Default.UcgsAddress;
            UcgsPort = Settings.Default.UcgsPort;
            UgcsAutomatic = Settings.Default.UgcsAutomatic;
            UgcsDirectConnection = !Settings.Default.UgcsAutomatic;

            VideoServerAddress = Settings.Default.VideoServerAddress;
            VideoServerPort = Settings.Default.VideoServerPort;
            VideoServerAutomatic = Settings.Default.VideoServerAutomatic;
            VideoServerDirectConnection = !Settings.Default.VideoServerAutomatic;

            Bitrate = Settings.Default.Bitrate;
            BitrateAutomatic = Settings.Default.BitrateAutomatic;
            BitrateManual = !Settings.Default.BitrateAutomatic;
            HardwareDecodingEnable = Settings.Default.HardwareDecodingEnable;
            HardwareDecodingDisable = !Settings.Default.HardwareDecodingEnable;

            CustomVideoSourceUri = Settings.Default.CustomVideoSourceUri;
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
                if (value && !_ugcsAutomatic)
                {
                    if (!IPAddress.TryParse(UcgsAddress, out var ip) || UcgsAddress.Split('.').Length != 4)
                    {
                        UcgsAddress = Settings.Default.UcgsAddress;
                    }
                    if (UcgsPort == null || UcgsPort < 1 || UcgsPort > 65535)
                    {
                        UcgsPort = Settings.Default.UcgsPort;
                    }
                }
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

        public int? _ugcsPort;
        public int? UcgsPort
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
                if (value && !_videoServerAutomatic)
                {
                    if (!IPAddress.TryParse(VideoServerAddress, out var ip) || VideoServerAddress.Split('.').Length != 4)
                    {
                        VideoServerAddress = Settings.Default.VideoServerAddress;
                    }
                    if (VideoServerPort == null || VideoServerPort < 1 || VideoServerPort > 65535)
                    {
                        VideoServerPort = Settings.Default.VideoServerPort;
                    }
                }
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

        public int? _videoServerPort;
        public int? VideoServerPort
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

        private string _customVideoSourceUri;
        public string CustomVideoSourceUri
        {
            get { return _customVideoSourceUri; }
            set
            {
                if (value != _customVideoSourceUri)
                {
                    _customVideoSourceUri = value;
                    NotifyOfPropertyChange(() => CustomVideoSourceUri);
                }
            }
        }

        public bool _bitrateAutomatic;
        public bool BitrateAutomatic
        {
            get
            {
                return _bitrateAutomatic;
            }
            set
            {
                if (value && !_bitrateAutomatic)
                {
                    if (Bitrate < 1 || Bitrate > 25)
                    {
                        Bitrate = Settings.Default.Bitrate;
                    }
                }
                _bitrateAutomatic = value;
                NotifyOfPropertyChange(() => BitrateAutomatic);
            }
        }

        public bool _bitrateManual;
        public bool BitrateManual
        {
            get
            {
                return _bitrateManual;
            }
            set
            {
                _bitrateManual = value;
                NotifyOfPropertyChange(() => BitrateManual);
            }
        }

        public int? _bitrate;
        public int? Bitrate
        {
            get
            {
                return _bitrate;
            }
            set
            {
                _bitrate = value;
                NotifyOfPropertyChange(() => Bitrate);
            }
        }

        public bool _hardwareDecodingEnable;
        public bool HardwareDecodingEnable
        {
            get
            {
                return _hardwareDecodingEnable;
            }
            set
            {
                _hardwareDecodingEnable = value;
                NotifyOfPropertyChange(() => HardwareDecodingEnable);
            }
        }

        public bool _hardwareDecodingDisable;
        public bool HardwareDecodingDisable
        {
            get
            {
                return _hardwareDecodingDisable;
            }
            set
            {
                _hardwareDecodingDisable = value;
                NotifyOfPropertyChange(() => HardwareDecodingDisable);
            }
        }

        private string GetError()
        {
            if (TailNumber == null || string.IsNullOrEmpty(TailNumber.Trim()))
            {
                return Resources.ErrorTail;
            }
            if (UgcsAutomatic == false)
            {
                if (!IPAddress.TryParse(UcgsAddress, out var ip) || UcgsAddress.Split('.').Length != 4)
                {
                    return Resources.UgcsIp;
                }
                if (UcgsPort == null || UcgsPort < 1 || UcgsPort > 65535)
                {
                    return Resources.UgcsPort;
                }
            }
            if (VideoServerAutomatic == false)
            {
                if (!IPAddress.TryParse(VideoServerAddress, out var ip) || VideoServerAddress.Split('.').Length != 4)
                {
                    return Resources.VideoServerIp;
                }
                if (VideoServerPort == null || VideoServerPort < 1 || VideoServerPort > 65535)
                {
                    return Resources.VideoServerPort;
                }
            }
            if (BitrateAutomatic == false)
            {
                if (Bitrate == null ||  Bitrate < 1 || Bitrate > 25)
                {
                    return Resources.BitrateError;
                }
            }

            string customVideoSourceUri = CustomVideoSourceUri;
            if (!String.IsNullOrWhiteSpace(CustomVideoSourceUri))
            {
                if (!Uri.IsWellFormedUriString(customVideoSourceUri, UriKind.Absolute))
                    return Resources.InvalidCustomVideoSourceURI;
            }


            return string.Empty;
        }

        public void SaveSettings(Window wnd)
        {
            string error = GetError();
            if (!string.IsNullOrEmpty(error))
            {
                MessageBox.Show(error, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }
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
                Settings.Default.UcgsPort = UcgsPort.GetValueOrDefault();
                if (!UgcsAutomatic)
                { 
                    changed.Add("UcgsAddress");
                }
            }
            if (VideoServerAutomatic != Settings.Default.VideoServerAutomatic)
            {
                Settings.Default.VideoServerAutomatic = VideoServerAutomatic;
                changed.Add("VideoServerAutomatic");
            }
            if (VideoServerAddress != Settings.Default.VideoServerAddress || VideoServerPort != Settings.Default.VideoServerPort)
            {
                Settings.Default.VideoServerAddress = VideoServerAddress;
                Settings.Default.VideoServerPort = VideoServerPort.GetValueOrDefault();
                if (!VideoServerAutomatic)
                {
                    changed.Add("VideoServerAddress");
                }
            }
            if (BitrateAutomatic != Settings.Default.BitrateAutomatic)
            {
                Settings.Default.BitrateAutomatic = BitrateAutomatic;
                changed.Add("BitrateAutomatic");
            }
            if (Bitrate != Settings.Default.Bitrate)
            {
                Settings.Default.Bitrate = Bitrate.GetValueOrDefault();
                if (!BitrateAutomatic)
                {
                    changed.Add("Bitrate");
                }
            }
            if (HardwareDecodingEnable != Settings.Default.HardwareDecodingEnable)
            {
                Settings.Default.HardwareDecodingEnable = HardwareDecodingEnable;
                changed.Add("HardwareDecodingEnable");
            }

            string customVideoSourceUri = CustomVideoSourceUri?.Trim();
            if (Settings.Default.CustomVideoSourceUri != customVideoSourceUri)
            {
                Settings.Default.CustomVideoSourceUri = customVideoSourceUri;
                changed.Add(nameof(Settings.CustomVideoSourceUri));
            }

            Settings.Default.Save();
            onSave?.Invoke(changed);
            wnd.Close();
        }
    }
}

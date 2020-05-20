using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using System.Timers;
using System.Windows;
using System.Windows.Controls;
using Caliburn.Micro;
using FFmpeg.AutoGen;
using SSDPDiscoveryService;
using UcsService;
using UcsService.DTO;
using UcsService.Enums;
using Unosquare.FFME.Common;
using VideoSources;
using VideoSources.DTO;
using VideoTransmitter.Enums;
using VideoTransmitter.Properties;
using VideoTransmitter.Views;


namespace VideoTransmitter.ViewModels
{
    public partial class MainViewModel : Caliburn.Micro.PropertyChangedBase
    {
        private Timer telemetryTimer;

        private Timer ucsConnectionTimer;
        private Timer videoServerConnectionTimer;

        private Timer mediaTimer;

        private const string UCS_SERVER_TYPE = "ugcs:hci-server";

        private Uri urtpServer = null;
        public const string UGCS_VIDEOSERVER_URTP_ST = "ugcs:video-server:input:urtp";

        private IDiscoveryService _discoveryService;
        private ConnectionService _ucsConnectionService;
        private VehicleListener _vehicleListener;
        private TelemetryListener _telemetryListener;
        private VehicleService _vehicleService;
        private VideoSourcesService _videoSourcesService;
        private Unosquare.FFME.MediaElement m_MediaElement;
        private IWindowManager _iWindowManager;
        private long _lastPacketRead = 0;
        private int LastPacketReadTimeout = 5000;

        public unsafe MainViewModel(DiscoveryService ds,
            ConnectionService cs,
            VehicleListener vl,
            TelemetryListener tl,
            VehicleService vs,
            VideoSourcesService vss,
            IWindowManager manager)
        {
            _iWindowManager = manager;
            _videoSourcesService = vss;
            _vehicleService = vs;
            _vehicleListener = vl;
            _telemetryListener = tl;
            _ucsConnectionService = cs;
            _discoveryService = ds;
            lock (videoSourcesListLocker)
            {
                var videoList = _videoSourcesService.GetVideoSources();
                VideoSourcesList = new ObservableCollection<VideoSourceDTO>(videoList);
                var defaultVideo = videoList.FirstOrDefault(v => v.Name == Settings.Default.LastCapureDevice);
                if (defaultVideo != null)
                {
                    SelectedVideoSource = defaultVideo;
                }
            }
            _ucsConnectionService.Connected += ucsConnection_onConnected;
            _ucsConnectionService.Disconnected += ucsConnection_onDisconnected;
            _videoSourcesService.SourcesChanged += videoSources_onChanged;
            _discoveryService.StartListen();

            ucsConnectionTimer = new Timer(500);
            ucsConnectionTimer.Elapsed += OnUcsConnection;
            ucsConnectionTimer.AutoReset = true;
            ucsConnectionTimer.Enabled = true;

            videoServerConnectionTimer = new Timer(500);
            videoServerConnectionTimer.Elapsed += OnVideoServerConnection;
            videoServerConnectionTimer.AutoReset = true;
            videoServerConnectionTimer.Enabled = true;

            mediaTimer = new Timer(500);
            mediaTimer.Elapsed += OnMediaReceiveTimer;
            mediaTimer.AutoReset = true;
            mediaTimer.Enabled = true;

            telemetryTimer = new Timer(1000);
            telemetryTimer.Elapsed += OnTelemetryTimer;
            telemetryTimer.AutoReset = true;
            telemetryTimer.Enabled = true;


        }
        private bool isConnecting = false;
        private void OnUcsConnection(Object source, ElapsedEventArgs e)
        {
            if (!_ucsConnectionService.IsConnected && !isConnecting)
            {
                isConnecting = true;
                startUcsConnection();
                isConnecting = false;
            }
        }

        private bool isRunningMediaCheck = false;
        private async void OnMediaReceiveTimer(Object source, ElapsedEventArgs e)
        {
            if (isRunningMediaCheck)
            {
                return;
            }
            isRunningMediaCheck = true;
            if (MediaElement != null && MediaElement.MediaState == MediaPlaybackState.Play)
            {
                if (DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - LastPacketReadTimeout > _lastPacketRead)
                {
                    await MediaElement.Close();
                    VideoMessage = $"Video Stopped from {SelectedVideoSource.Name}";
                    VideoMessageVisibility = Visibility.Visible;
                    VideoReady = CamVideo.NOT_READY;
                    _isStreaming = false;
                    NotifyOfPropertyChange(() => VideoServerStatus);
                    NotifyOfPropertyChange(() => VideoServerStatusText);
                    NotifyOfPropertyChange(() => TelemetryStatus);
                    NotifyOfPropertyChange(() => TelemetryStatusText);
                }
            }
            else if (MediaElement != null && MediaElement.MediaState == MediaPlaybackState.Close)
            {
                await StartScreenStreaming();
            }
            isRunningMediaCheck = false;
        }

        private void OnVideoServerConnection(Object source, ElapsedEventArgs e)
        {
            if (urtpServer == null)
            {
                if (Settings.Default.VideoServerAutomatic)
                {
                    if (!searhing)
                    {
                        searhing = true;
                        _discoveryService.TryFound(UGCS_VIDEOSERVER_URTP_ST, (location) =>
                        {
                            if (Settings.Default.VideoServerAutomatic)
                            {
                                urtpServer = location;
                                NotifyOfPropertyChange(() => VideoServerStatus);
                                NotifyOfPropertyChange(() => VideoServerStatusText);
                                NotifyOfPropertyChange(() => TelemetryStatus);
                                NotifyOfPropertyChange(() => TelemetryStatusText);
                            }
                            searhing = false;
                        });
                    }
                }
                else
                {
                    urtpServer = new Uri("urtp+connect://" + Settings.Default.VideoServerAddress + ":" + Settings.Default.VideoServerPort);
                    NotifyOfPropertyChange(() => VideoServerStatus);
                    NotifyOfPropertyChange(() => VideoServerStatusText);
                    NotifyOfPropertyChange(() => TelemetryStatus);
                    NotifyOfPropertyChange(() => TelemetryStatusText);
                }
            }
        }
        private void OnTelemetryTimer(Object source, ElapsedEventArgs e)
        {
            if (SelectedVehicle != null)
            {
                var telemetry = _telemetryListener.GetTelemetryById(SelectedVehicle.VehicleId);
                if (telemetry != null)
                {
                    double? altitude = telemetry.AltitudeAMSL;
                    double? longitude = telemetry.Longitude;
                    double? latitude = telemetry.Latitude;
                    string platformDesignation = SelectedVehicle.Name;
                    double? heading = telemetry.Heading;
                    double? pitch = telemetry.Pitch;
                    double? roll = telemetry.Roll;
                    double? sensorRelativeAzimuth = telemetry.PayloadHeading;
                    double? sensorRelativeElevation = telemetry.PayloadPitch;
                    double? sensorRelativeRoll = telemetry.PayloadRoll;
                }
                //TODO: send misp telemetry
                /*
        tlm.sensorHorizontalFov = mediaStreamerContainer.getSensorHorizontalFov();
        tlm.sensorVerticalFov = mediaStreamerContainer.getSensorVerticalFov();
        tlm.slantRange = mediaStreamerContainer.getSlantRange();
        */
            }
        }

        public void Connect(Uri address)
        {
            try
            {
                _ucsConnectionService.Connect(address, new UcsCredentials(string.Empty, string.Empty));
                NotifyOfPropertyChange(() => TelemetryStatus);
                NotifyOfPropertyChange(() => TelemetryStatusText);
            }
            catch
            {
                ucsConnection_onDisconnected(null, null);
            }
        }
        private void ucsConnection_onDisconnected(object sender, EventArgs e)
        {
            Execute.OnUIThreadAsync(() =>
            {
                lock (vehicleUpdateLocked)
                {
                    _vehicleList.Clear();
                }
                NotifyOfPropertyChange(() => VehicleList);
            });
            UcsConnection = "Disconnected";
        }

        private bool searhing = false;
        private void startUcsConnection()
        {
            if (Settings.Default.UgcsAutomatic)
            {
                if (!searhing)
                {
                    searhing = true;
                    _discoveryService.TryFound(UCS_SERVER_TYPE, (location) =>
                    {
                        if (Settings.Default.UgcsAutomatic)
                        {
                            Connect(location);
                        }
                        searhing = false;
                    });
                }
            }
            else
            {
                Connect(new Uri("tcp://"+Settings.Default.UcgsAddress+":"+ Settings.Default.UcgsPort));
            }
        }

        private void ucsConnection_onConnected(object sender, EventArgs args)
        {
            var cs = sender as ConnectionService;
            UcsConnection = string.Format("Connected. client ID: {0}", cs.ClientId);
            updateVehicleList(() =>
            {
                Task.Factory.StartNew(() =>
                {
                    _vehicleListener.SubscribeVehicle(updateVehicle);
                    _telemetryListener.SubscribeTelemtry();
                });
            });
        }

        private void videoSources_onChanged(object sender, EventArgs e)
        {
            List<VideoSourceDTO> sources = sender as List<VideoSourceDTO>;
            Execute.OnUIThreadAsync(() =>
            {
                bool mod = false;
                lock (videoSourcesListLocker)
                {
                    foreach (var source in sources)
                    {
                        if (!VideoSourcesList.Any(v => v.Name == source.Name))
                        {
                            VideoSourcesList.Add(source);
                            mod = true;
                        }
                    }
                    foreach (var source in _videoSourcesList)
                    {
                        if (!sources.Any(v => v.Name == source.Name))
                        {
                            VideoSourcesList.Remove(source);
                            mod = true;
                        }
                    }
                }

                if (mod)
                {
                    NotifyOfPropertyChange(() => VideoSourcesList);
                }
            });
        }

        private string ucsConnection = "Disconnected";
        public string UcsConnection
        {
            get
            {
                return ucsConnection;
            }
            set
            {
                ucsConnection = value;
                NotifyOfPropertyChange(() => UcsConnection);
                NotifyOfPropertyChange(() => TelemetryStatus);
                NotifyOfPropertyChange(() => TelemetryStatusText);
            }
        }

        private ObservableCollection<ClientVehicleDTO> _vehicleList = new ObservableCollection<ClientVehicleDTO>();
        public ObservableCollection<ClientVehicleDTO> VehicleList
        {
            get
            {
                return _vehicleList;
            }
            set
            {
                _vehicleList = value;
            }
        }

        private object videoSourcesListLocker = new object();
        private ObservableCollection<VideoSourceDTO> _videoSourcesList = new ObservableCollection<VideoSourceDTO>();
        public ObservableCollection<VideoSourceDTO> VideoSourcesList
        {
            get
            {
                return _videoSourcesList;
            }
            set
            {
                _videoSourcesList = value;
                NotifyOfPropertyChange(() => VideoSourcesList);
            }
        }


        private Object vehicleUpdateLocked = new Object();
        private void updateVehicle(ClientVehicleDTO vehicle, ModificationTypeDTO modType)
        {
            Execute.OnUIThreadAsync(() =>
            {
                bool mod = false;
                lock (vehicleUpdateLocked)
                {
                    switch (modType)
                    {
                        case ModificationTypeDTO.CREATED:
                            if (!_vehicleList.Any(v => v.VehicleId == vehicle.VehicleId))
                            {
                                _vehicleList.Add(vehicle);
                            }
                            mod = true;
                            break;
                        case ModificationTypeDTO.DELETED:
                            foreach (var vInList in _vehicleList.ToList())
                            {
                                if (vehicle.VehicleId == vInList.VehicleId)
                                {
                                    _vehicleList.Remove(vInList);
                                }
                            }
                            mod = true;
                            break;
                    }
                }
                if (mod)
                {                    
                    NotifyOfPropertyChange(() => VehicleList);
                }
            });
        }

        private void updateVehicleList(System.Action callback = null)
        {
            Task.Factory.StartNew(() =>
            {
                var vehicleList = _vehicleService.GetVehicles();
                Execute.OnUIThreadAsync(() =>
                {
                    var list = new List<int>();
                    foreach (var vInList in vehicleList)
                    {
                        if (!_vehicleList.Any(v => v.VehicleId == vInList.VehicleId))
                        {
                            _vehicleList.Add(vInList);
                        }
                        list.Add(vInList.VehicleId);
                    }

                    foreach (var vInList in _vehicleList.ToList())
                    {
                        if (!list.Any(l => l == vInList.VehicleId))
                        {
                            _vehicleList.Remove(vInList);
                        }
                    }
                    if (_vehicleList.Count == 0)
                    {
                        SelectedVehicle = null;
                    }
                    var defaultVehicle = VehicleList.FirstOrDefault(v => v.VehicleId.ToString() == Settings.Default.LastVehicleId);
                    if (defaultVehicle != null)
                    {
                        SelectedVehicle = defaultVehicle;
                    }
                    NotifyOfPropertyChange(() => VehicleList);
                    if (callback != null)
                    {
                        callback();
                    }
                });
            });
        }

        private ClientVehicleDTO _selectedVehicle;
        public ClientVehicleDTO SelectedVehicle
        {
            get
            {
                return _selectedVehicle;
            }
            set
            {
                if (_selectedVehicle != null && value != null && _selectedVehicle.VehicleId == value.VehicleId)
                {
                    return;
                }
                _selectedVehicle = value;
                Settings.Default.LastVehicleId = _selectedVehicle?.VehicleId.ToString();
                Settings.Default.Save();
                NotifyOfPropertyChange(() => SelectedVehicle);
                NotifyOfPropertyChange(() => TelemetryStatus);
                NotifyOfPropertyChange(() => TelemetryStatusText);
            }
        }

        private VideoSourceDTO _selectedVideoSource;
        public VideoSourceDTO SelectedVideoSource
        {
            get
            {
                return _selectedVideoSource;
            }
            set
            {
                if (_selectedVideoSource != null && value != null && _selectedVideoSource.Name == value.Name)
                {
                    return;
                }
                _selectedVideoSource = value;                
                Settings.Default.LastCapureDevice = _selectedVideoSource?.Name;
                Settings.Default.Save();
                VideoReady = CamVideo.NOT_READY;
                _isStreaming = false;
                NotifyOfPropertyChange(() => VideoServerStatus);
                if (_selectedVideoSource != null && viewLoaded)
                {
                    MediaElement.Close();
                }
                NotifyOfPropertyChange(() => SelectedVideoSource);
            }
        }

        public Unosquare.FFME.MediaElement MediaElement
        {
            get
            {
                return m_MediaElement;
            }
        }

        private async Task StartScreenStreaming()
        {
            if (MediaElement == null || SelectedVideoSource == null)
            {
                return;
            }
            if (MediaElement != null)
            {
                if (!await MediaElement.Open(new Uri($"device://dshow/?video={SelectedVideoSource.Name}")))
                {
                    VideoMessage = $"Failed to load video from {SelectedVideoSource.Name}";
                    VideoMessageVisibility = Visibility.Visible;
                    VideoReady = CamVideo.NOT_READY;
                    _isStreaming = false;
                    NotifyOfPropertyChange(() => VideoServerStatus);
                    NotifyOfPropertyChange(() => VideoServerStatusText);
                }
            }
        }

        private VideoServerStatus videoStreamingStatus = VideoServerStatus.NOT_READY_TO_STREAM;
        private bool _isStreaming = false;
        public void StartStreaming()
        {
            Task.Factory.StartNew(() =>
            {
                if (!_isStreaming)
                {
                    videoStreamingStatus = VideoServerStatus.INITIALIZING;
                    NotifyOfPropertyChange(() => VideoServerStatus);
                    NotifyOfPropertyChange(() => VideoServerStatusText);
                    NotifyOfPropertyChange(() => TelemetryStatus);
                    NotifyOfPropertyChange(() => TelemetryStatusText);
                    System.Threading.Thread.Sleep(1000);
                    _isStreaming = true;
                    videoStreamingStatus = VideoServerStatus.STREAMING;
                }
                else
                {
                    videoStreamingStatus = VideoServerStatus.FINISHED;
                    _isStreaming = false;
                }
                NotifyOfPropertyChange(() => VideoServerStatus);
                NotifyOfPropertyChange(() => VideoServerStatusText);
                NotifyOfPropertyChange(() => TelemetryStatus);
                NotifyOfPropertyChange(() => TelemetryStatusText);
            });            
        }

        private bool viewLoaded = false;
        public void ViewLoaded()
        {
            viewLoaded = true;
            m_MediaElement = (Application.Current.MainWindow as MainView)?.Media;
            MediaElement.PacketRead += packedRead;
            MediaElement.MediaInitializing += OnMediaInitializing;
            MediaElement.MediaOpening += OnMediaOpening;
            MediaElement.MediaOpened += OnMediaOpened;
        }
        private unsafe void packedRead(object sender, Unosquare.FFME.Common.PacketReadEventArgs e)
        {
            if (MediaElement.MediaState == MediaPlaybackState.Play)
            {
                _lastPacketRead = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            }
            // Capture stream here
            if (e.Packet != null)
            {
                Debug.WriteLine(e.Packet->size);
            }
        }
        public void SettingsWindows()
        {
            _iWindowManager.ShowDialog(new SettingsViewModel(onSettingsSaved));
        }

        public void onSettingsSaved(HashSet<String> changed)
        {
            if (changed.Contains("UgcsAutomatic") || changed.Contains("UcgsAddress")) {
                if (_ucsConnectionService.IsConnected)
                {
                    _ucsConnectionService.Disconnect();
                }
            }

            if (changed.Contains("VideoServerAutomatic") || changed.Contains("VideoServerAddress"))
            {
                urtpServer = null;
                NotifyOfPropertyChange(() => VideoServerStatus);
                NotifyOfPropertyChange(() => VideoServerStatusText);
                NotifyOfPropertyChange(() => TelemetryStatus);
                NotifyOfPropertyChange(() => TelemetryStatusText);

            }
        }

        private void OnMediaOpening(object sender, MediaOpeningEventArgs e)
        {
            Execute.OnUIThreadAsync(() =>
            {
                VideoMessage = "Loading video";
                VideoMessageVisibility = Visibility.Visible;
            });
            VideoReady = CamVideo.NOT_READY;
            _isStreaming = false;
            NotifyOfPropertyChange(() => VideoServerStatus);
            NotifyOfPropertyChange(() => VideoServerStatusText);
        }
        private void OnMediaOpened(object sender, MediaOpenedEventArgs e)
        {
            Execute.OnUIThreadAsync(() =>
            {
                VideoMessage = string.Empty;
                VideoMessageVisibility = Visibility.Hidden;
            });
            VideoReady = CamVideo.READY;
            NotifyOfPropertyChange(() => VideoServerStatus);
            NotifyOfPropertyChange(() => VideoServerStatusText);
        }
        private void OnMediaInitializing(object sender, MediaInitializingEventArgs e)
        {
            Execute.OnUIThreadAsync(() =>
            {
                VideoMessage = "Loading video";
                VideoMessageVisibility = Visibility.Visible;
            });
            VideoReady = CamVideo.NOT_READY;
            _isStreaming = false;
            NotifyOfPropertyChange(() => VideoServerStatus);
            NotifyOfPropertyChange(() => VideoServerStatusText);
        }

        private string _videoMessage = string.Empty;
        public string VideoMessage
        {
            get
            {
                return _videoMessage;
            }
            set
            {
                _videoMessage = value;
                NotifyOfPropertyChange(() => VideoMessage);
            }
        }

        private Visibility _videoMessageVisibility = Visibility.Hidden;
        public Visibility VideoMessageVisibility
        {
            get
            {
                return _videoMessageVisibility;
            }
            set
            {
                _videoMessageVisibility = value;
                NotifyOfPropertyChange(() => VideoMessageVisibility);
            }
        }


        public TelemetryStatus TelemetryStatus
        {
            get
            {
                if (SelectedVehicle == null 
                    || SelectedVideoSource == null 
                    || urtpServer == null
                    || !_ucsConnectionService.IsConnected)
                {
                    return TelemetryStatus.NOT_READY_TO_STREAM;
                }
                else if (_isStreaming)
                {
                    return TelemetryStatus.STREAMING;
                }
                else
                {
                    return TelemetryStatus.READY_TO_STREAM;
                }
            }
        }

        public VideoServerStatus VideoServerStatus
        {
            get
            {
                if (SelectedVideoSource == null
                    || urtpServer == null
                    || VideoReady == CamVideo.NOT_READY)
                {
                    return VideoServerStatus.NOT_READY_TO_STREAM;
                }
                else if (videoStreamingStatus == VideoServerStatus.INITIALIZING)
                {
                    return VideoServerStatus.INITIALIZING;
                }
                else if (videoStreamingStatus == VideoServerStatus.FINISHED)
                {
                    return VideoServerStatus.FINISHED;
                }
                else if (_isStreaming)
                {
                    return VideoServerStatus.STREAMING;
                }
                else
                {
                    return VideoServerStatus.READY_TO_STREAM;
                }
            }
        }

        public CamVideo VideoReady { get; set; } = CamVideo.NOT_READY;

        public string TelemetryStatusText
        {
            get
            {
                if (urtpServer == null)
                {
                    return "VideoServer not discovered";
                }
                if (!_ucsConnectionService.IsConnected)
                {
                    return "UgCS Server is not connected";
                }
                if (SelectedVideoSource == null)
                {
                    return "Video source not selected";
                }
                if (SelectedVehicle == null)
                {
                    return "Vehicle is not selected";
                }
                if (_isStreaming)
                {
                    return "Streaming";
                }
                return string.Format("Ready to stream telemetry to VideoServer {0}", urtpServer.Host + ":" + urtpServer.Port);
            }
        }
        public string VideoServerStatusText
        {
            get
            {
                if (urtpServer == null)
                {
                    return "VideoServer not discovered";
                }
                if (SelectedVideoSource == null)
                {
                    return "Video source is not selected";
                }
                if (VideoReady == CamVideo.NOT_READY)
                {
                    return "Video source is not streaming video";
                }
                if (videoStreamingStatus == VideoServerStatus.INITIALIZING)
                {
                    return "Stream initializing";
                }
                if (_isStreaming)
                {
                    return "Streaming";
                }
                return string.Format("Ready to stream to VideoServer {0}", urtpServer.Host + ":" + urtpServer.Port);
            }
        }

        public bool SettingsButtonEnabled
        {
            get
            {
                if (_isStreaming)
                {
                    return false;
                }
                return true;
            }
        }

    }
}

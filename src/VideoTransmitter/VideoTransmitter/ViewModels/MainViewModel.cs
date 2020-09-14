using Caliburn.Micro;
using FFmpeg.AutoGen;
using SSDPDiscoveryService;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Timers;
using System.Windows;
using System.Windows.Controls;
using UcsService;
using UcsService.DTO;
using UcsService.Enums;
using Ugcs.Video.MispStreamer;
using Ugcs.Video.Tools;
using Unosquare.FFME.Common;
using VideoSources;
using VideoSources.DTO;
using VideoTransmitter.Enums;
using VideoTransmitter.Models;
using VideoTransmitter.Properties;
using VideoTransmitter.Views;


using Interlocked = System.Threading.Interlocked;



namespace VideoTransmitter.ViewModels
{
    public partial class MainViewModel : Caliburn.Micro.PropertyChangedBase
    {
        private static readonly TimeSpan BPS_MEASURE_INTERVAL = TimeSpan.FromSeconds(3);

        private log4net.ILog _log = log4net.LogManager.GetLogger(typeof(MainViewModel));

        private Timer telemetryTimer;

        private Timer ucsConnectionTimer;
        private Timer videoServerConnectionTimer;

        private Timer mediaTimer;

        private const string UCS_SERVER_TYPE = "ugcs:hci-server";

        private Uri urtpServer = null;
        public const string UGCS_VIDEOSERVER_URTP_ST = "ugcs:video-server:input:urtp";

        private VideoSource _defaultVideoDevice;
        private static readonly Uri EMPTY_DEVICE_URI = new Uri("device://empty");

        private ClientVehicle _defaultVehicle;
        private const int EMPTY_VEHICLE_ID = 0;

        private IDiscoveryService _discoveryService;
        private ConnectionService _ucsConnectionService;
        private VehicleListener _vehicleListener;
        private TelemetryListener _telemetryListener;
        private VehicleService _vehicleService;
        private VideoSourcesService _videoSourcesService;
        private Unosquare.FFME.MediaElement m_MediaElement;
        private IWindowManager _iWindowManager;
        private MispVideoStreamer _mispStreamer;
        private EncodingWorker _encoding;
        private StreamWithBitrateControl _speedometer;

        //VideoServer internal statuses
        private VideoServerStatus _videoStreamingStatus = VideoServerStatus.NOT_READY_TO_STREAM;
        private bool _isStreaming;
        private bool _hasConnected = false;
        private object _startStopLocker = new object();
        private Bitrate _encodingBitrate;


        public Bitrate EncodingBitrate
        {
            get
            {
                return _encodingBitrate;
            }
            set
            {
                if (value.Equals(_encodingBitrate))
                    return;
                _encodingBitrate = value;
                NotifyOfPropertyChange(() => EncodingBitrate);
            }
        }


        public unsafe MainViewModel(DiscoveryService ds,
            ConnectionService cs,
            VehicleListener vl,
            TelemetryListener tl,
            VehicleService vs,
            VideoSourcesService vss,
            IWindowManager manager)
        {
            _log.Debug("Main view model initialized");
            _iWindowManager = manager;
            _videoSourcesService = vss;
            _vehicleService = vs;
            _vehicleListener = vl;
            _telemetryListener = tl;
            _ucsConnectionService = cs;
            _discoveryService = ds;

            _telemetryListener.TelemetryVideoUrlChanged += telemetryVideo_onChanged;
            _discoveryService.AddToListen(UCS_SERVER_TYPE);
            _discoveryService.AddToListen(UGCS_VIDEOSERVER_URTP_ST);
            _discoveryService.ServiceLost += onSsdpServiceLost;


            _defaultVideoDevice = new VideoSource
            (
                name: Resources.Nodevice,
                uri: EMPTY_DEVICE_URI
            );
            VideoSources.Add(_defaultVideoDevice);
            resetDefaultVideoSeource(_defaultVideoDevice);

            _defaultVehicle = new ClientVehicle()
            {
                Name = Resources.Novehicle,
                VehicleId = EMPTY_VEHICLE_ID,
                IsConnected = true
            };
            VehicleList.Add(_defaultVehicle);
            resetDefaultSelectedVehicle(_defaultVehicle);

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

            loadMediaFileList();
        }

        private void loadMediaFileList()
        {
            string mediaPath = Path.Combine(
                Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location),
                "Media");
            if (!Directory.Exists(mediaPath))
                return;

            foreach (string filePath in Directory.GetFiles(mediaPath))
            {
                VideoSources.Add(
                    new VideoSource(
                        name: Path.GetFileName(filePath),
                        uri: new Uri("file://" + filePath)
                    ));
            }
        }

        private static VideoSource toVideoSource(VideoDeviceDTO device)
        {
            if (device.Type == SourceType.VEHICLE)
            {
                return new VideoSource(
                    name: device.Id,
                    uri: new Uri(device.Name));
            }
            else
            {
                return new VideoSource(
                    name: device.Name,
                    uri: new Uri($"device://dshow/?video={device.Name}"));
            }
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
            if (MediaElement != null && MediaElement.MediaState == MediaPlaybackState.Close
                && !isNullOrEmpty(SelectedVideoSource))
            {
                _log.Info("Try start new media");
                await startScreenStreaming();
            }
            isRunningMediaCheck = false;
        }

        private bool isNullOrEmpty(VideoSource vs)
        {
            return vs == null || vs.Uri == EMPTY_DEVICE_URI;
        }

        private volatile int _videoServerSearchSessions = 0;
        private void OnVideoServerConnection(Object source, ElapsedEventArgs e)
        {
            if (urtpServer != null)
                return;

            if (!Settings.Default.VideoServerAutomatic)
            {
                lock (_startStopLocker)
                {
                    urtpServer = new Uri("urtp+connect://" + Settings.Default.VideoServerAddress + ":" + Settings.Default.VideoServerPort);
                }
                _log.Info(string.Format("Direct connection used to videoserver {0}", urtpServer.AbsolutePath));
                updateVideoAndTelemetryStatuses();
                return;
            }

            if (Interlocked.Increment(ref _videoServerSearchSessions) > 1)
            {
                Interlocked.Decrement(ref _videoServerSearchSessions);
                return;
            }

            _discoveryService.TryFound(UGCS_VIDEOSERVER_URTP_ST, (location) =>
            {
                if (Settings.Default.VideoServerAutomatic)
                {
                    lock (_startStopLocker)
                    {
                        urtpServer = location;
                    }
                    updateVideoAndTelemetryStatuses();
                    _log.Info(string.Format("Found new videoserver {0}", urtpServer.AbsolutePath));
                }
                Interlocked.Decrement(ref _videoServerSearchSessions);
            });
        }
        private void OnTelemetryTimer(Object source, ElapsedEventArgs e)
        {
            if (SelectedVehicle != null && SelectedVehicle.VehicleId != EMPTY_VEHICLE_ID && _mispStreamer != null && _isStreaming)
            {
                var telemetry = _telemetryListener.GetTelemetryById(SelectedVehicle.VehicleId);
                if (telemetry != null)
                {
                    double? heading = null;
                    if (telemetry.Heading != null)
                    {
                        heading = (telemetry.Heading + 2 * Math.PI) % (2 * Math.PI);
                    }
                    double? payloadHeading = null;
                    if (telemetry.PayloadHeading != null)
                    {
                        payloadHeading = (telemetry.PayloadHeading + 2 * Math.PI) % (2 * Math.PI);
                    }
                    double? payloadPitch = null;
                    if (telemetry.PayloadPitch != null)
                    {
                        payloadPitch = telemetry.PayloadPitch;
                        while (payloadPitch > Math.PI) payloadPitch -= 2 * Math.PI;
                        while (payloadPitch < -Math.PI) payloadPitch += 2 * Math.PI;
                    }
                    double? payloadRoll = null;
                    if (telemetry.PayloadRoll != null)
                    {
                        payloadRoll = (telemetry.PayloadRoll + 2 * Math.PI) % (2 * Math.PI);
                    }
                    MispTelemetry tlm = new MispTelemetry()
                    {
                        Altitude = telemetry.AltitudeAMSL,
                        Longitude = telemetry.Longitude,
                        Latitude = telemetry.Latitude,
                        Heading = heading,
                        PlatformDesignation = SelectedVehicle.Name,
                        Pitch = telemetry.Pitch,
                        Roll = telemetry.Roll,
                        SensorRelativeAzimuth = payloadHeading,
                        SensorRelativeElevation = payloadPitch,
                        SensorRelativeRoll = payloadRoll
                    };
                    _mispStreamer.FeedTelemetry(tlm);
                }
            }
        }

        public void Connect(Uri address)
        {
            try
            {
                _ucsConnectionService.Connect(address, new UcsCredentials(string.Empty, string.Empty));
                updateVideoAndTelemetryStatuses();
                _log.Error("UgCS connected");
            }
            catch (Exception e)
            {
                _log.Error("Could not connect to UgCS");
                _log.Error(e);
                ucsConnection_onDisconnected(null, null);
            }
        }
        private void ucsConnection_onDisconnected(object sender, EventArgs e)
        {
            _vehicleListener.UnsubscribeAll();
            Execute.OnUIThreadAsync(() =>
            {
                if (SelectedVehicle == null || _defaultVehicle.VehicleId != SelectedVehicle.VehicleId)
                {
                    resetDefaultSelectedVehicle(_defaultVehicle);
                }
                lock (vehicleUpdateLocked)
                {
                    foreach (var vInList in _vehicleList.Skip(1).ToList())
                    {
                        _vehicleList.Remove(vInList);
                    }
                }
                NotifyOfPropertyChange(() => VehicleList);
            });
            _videoSourcesService.ClearVehicleVideoSource();
            _log.Info("UgCS server disconnected");
            updateVideoAndTelemetryStatuses();
        }

        private volatile int _ucsSearchSessions = 0;
        private void startUcsConnection()
        {
            if (!Settings.Default.UgcsAutomatic)
            {
                var uri = new Uri("tcp://" + Settings.Default.UcgsAddress + ":" + Settings.Default.UcgsPort);
                _log.Info(string.Format("Direct connection used to UgCS server {0}", uri.OriginalString));
                Connect(uri);
                return;
            }

            if (Interlocked.Increment(ref _ucsSearchSessions) > 1)
            {
                Interlocked.Decrement(ref _ucsSearchSessions);
                return;
            }

            _discoveryService.TryFound(UCS_SERVER_TYPE, (location) =>
                {
                    if (Settings.Default.UgcsAutomatic)
                    {
                        _log.Info(string.Format("Found new UgCS server {0}", location.OriginalString));
                        Connect(location);
                    }
                    Interlocked.Decrement(ref _ucsSearchSessions);
                });
        }

        private void ucsConnection_onConnected(object sender, EventArgs args)
        {
            _log.Debug("ucsConnection_onConnected called");
            var cs = sender as ConnectionService;
            updateVideoAndTelemetryStatuses();
            updateVehicleList(() =>
            {
                Task.Factory.StartNew(() =>
                {
                    _vehicleListener.SubscribeVehicle(updateVehicle);
                    _telemetryListener.SubscribeTelemtry(downlinkUpdated);
                });
            });
        }
        private void videoSources_onChanged(object sender, EventArgs e)
        {
            _log.Debug("videoSources_onChanged called");

            VideoSource[] devices = ((List<VideoDeviceDTO>)sender)
                .Select(d => toVideoSource(d))
                .ToArray();

            Execute.OnUIThreadAsync(() =>
            {
                bool updateToDefault = false;
                lock (videoSourcesListLocker)
                {
                    var added = devices.Except(VideoSources);
                    var removed = VideoSources
                                    .Skip(1)
                                    .Where(x => x.Uri.Scheme != "file")
                                    .Except(devices).ToList();

                    foreach (var d in added)
                    {
                        VideoSources.Add(d);
                    }
                    foreach (var d in removed)
                    {
                        if (SelectedVideoSource != null && SelectedVideoSource.Name == d.Name)
                        {
                            updateToDefault = true;
                        }
                        VideoSources.Remove(d);
                    }
                }

                if (updateToDefault)
                {
                    resetDefaultVideoSeource(_defaultVideoDevice);
                }
                var defaultVideo = VideoSources.FirstOrDefault(v => v.Name == Settings.Default.LastCapureDevice);
                if (defaultVideo != null)
                {
                    SelectedVideoSource = defaultVideo;
                }
                else
                {
                    var defaultVideoLastKnown = VideoSources.FirstOrDefault(v => v.Name == _lastKnownName);
                    if (defaultVideoLastKnown != null)
                    {
                        SelectedVideoSource = defaultVideoLastKnown;
                    }
                }
            });
        }
        private void telemetryVideo_onChanged(VideoSourceChangedDTO vsc)
        {
            _videoSourcesService.AddOrUpdateVehicleVideoSource(new VideoDeviceDTO()
            {
                Id = VideoDeviceDTO.GenerateId(vsc.VehicleId.ToString(), vsc.VideoSourceName),
                VehicleId = vsc.VehicleId,
                Name = vsc.VideoSourceName,
                Type = SourceType.VEHICLE
            });
        }

        private void onSsdpServiceLost(string serviceType, string location)
        {
            if (Settings.Default.VideoServerAutomatic == true
                && serviceType == UGCS_VIDEOSERVER_URTP_ST
                && urtpServer != null
                && location == urtpServer.OriginalString)
            {
                lock (_startStopLocker)
                {
                    urtpServer = null;
                }
                stopMisp();
            }
        }

        private ObservableCollection<ClientVehicle> _vehicleList = new ObservableCollection<ClientVehicle>();
        public ObservableCollection<ClientVehicle> VehicleList
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
        public ObservableCollection<VideoSource> VideoSources { get; } = new ObservableCollection<VideoSource>();

        private void downlinkUpdated(int vehicleId, bool downlink)
        {
            _log.Info(string.Format("Vehicle downlink updated id:{0}  downlink:{1}", vehicleId, downlink.ToString()));
            Task.Factory.StartNew(() =>
            {
                var vh = _vehicleList.FirstOrDefault(v => v.VehicleId == vehicleId);
                if (vh != null)
                {
                    vh.IsConnected = downlink;
                    updateVideoAndTelemetryStatuses();
                }
            });
        }
        private Object vehicleUpdateLocked = new Object();
        private void updateVehicle(ClientVehicleDTO vehicle, ModificationTypeDTO modType)
        {
            _log.Info(string.Format("Vehicle update call {0} {1}", vehicle.Name, modType.ToString()));
            Execute.OnUIThreadAsync(() =>
            {
                bool mod = false;
                lock (vehicleUpdateLocked)
                {
                    switch (modType)
                    {
                        case ModificationTypeDTO.CREATED:
                        case ModificationTypeDTO.UPDATED:
                            var vh = _vehicleList.FirstOrDefault(v => v.VehicleId == vehicle.VehicleId);
                            if (vh == null)
                            {

                                ClientVehicle cv = new ClientVehicle()
                                {
                                    Name = vehicle.Name,
                                    TailNumber = vehicle.TailNumber,
                                    VehicleId = vehicle.VehicleId
                                };
                                var telemetry = _telemetryListener.GetTelemetryById(cv.VehicleId);
                                if (telemetry != null)
                                {
                                    cv.IsConnected = telemetry.DownlinkPresent;
                                }
                                _vehicleList.Add(cv);
                            }
                            else
                            {
                                vh.Name = vehicle.Name;
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
                    if (SelectedVehicle == null)
                    {
                        resetDefaultSelectedVehicle(_defaultVehicle);
                    }
                    updateVideoAndTelemetryStatuses();
                    NotifyOfPropertyChange(() => VehicleList);
                    NotifyOfPropertyChange(() => SelectedVehicle);
                }
            });
        }

        private void updateVehicleList(System.Action callback = null)
        {
            _log.Info("Vehicle list update call");
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
                            ClientVehicle cv = new ClientVehicle()
                            {
                                Name = vInList.Name,
                                TailNumber = vInList.TailNumber,
                                VehicleId = vInList.VehicleId
                            };
                            var telemetry = _telemetryListener.GetTelemetryById(cv.VehicleId);
                            if (telemetry != null)
                            {
                                cv.IsConnected = telemetry.DownlinkPresent;
                            }
                            _vehicleList.Add(cv);
                        }
                        list.Add(vInList.VehicleId);
                    }

                    foreach (var vInList in _vehicleList.Skip(1).ToList())
                    {
                        if (!list.Any(l => l == vInList.VehicleId))
                        {
                            _vehicleList.Remove(vInList);
                        }
                    }
                    if (_vehicleList.Count == 0)
                    {
                        resetDefaultSelectedVehicle(_defaultVehicle);
                    }
                    else
                    {
                        var defaultVehicle = _vehicleList.FirstOrDefault(v => v.VehicleId.ToString() == Settings.Default.LastVehicleId);
                        if (defaultVehicle != null)
                        {
                            SelectedVehicle = defaultVehicle;
                        }
                    }
                    updateVideoAndTelemetryStatuses();
                    NotifyOfPropertyChange(() => VehicleList);
                    if (callback != null)
                    {
                        callback();
                    }
                });
            });
        }

        private ClientVehicle _selectedVehicle;
        public ClientVehicle SelectedVehicle
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
                if (_selectedVehicle != null)
                {
                    Settings.Default.LastVehicleId = _selectedVehicle.VehicleId.ToString();
                    Settings.Default.Save();
                }
                NotifyOfPropertyChange(() => SelectedVehicle);
                if (_selectedVehicle != null && _selectedVehicle.VehicleId != EMPTY_VEHICLE_ID)
                {
                    _log.Info(string.Format("Vehicle selected {0}", _selectedVehicle.Name));
                    var telemetry = _telemetryListener.GetTelemetryById(SelectedVehicle.VehicleId);
                    if (telemetry != null && !string.IsNullOrEmpty(telemetry.VideoStreamUrl))
                    {
                        var videoSource = VideoSources.FirstOrDefault(v => v.Name == VideoDeviceDTO.GenerateId(SelectedVehicle.VehicleId.ToString(), telemetry.VideoStreamUrl));
                        if (videoSource != null)
                        {
                            SelectedVideoSource = videoSource;
                        }
                    }
                }
                else
                {
                    _log.Info("Empty vehicle selected");
                }
                updateVideoAndTelemetryStatuses();
            }
        }

        public void resetDefaultSelectedVehicle(ClientVehicle videoSource)
        {
            _selectedVehicle = videoSource;
            updateVideoAndTelemetryStatuses();
            NotifyOfPropertyChange(() => SelectedVehicle);
        }

        private string _lastKnownName = string.Empty;
        private VideoSource _selectedVideoSource;
        public VideoSource SelectedVideoSource
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
                if (_selectedVideoSource != null)
                {
                    Settings.Default.LastCapureDevice = _selectedVideoSource.Name;
                    Settings.Default.Save();
                }
                VideoReady = CamVideo.NOT_READY;
                updateVideoAndTelemetryStatuses();
                if (_selectedVideoSource != null && _selectedVideoSource.Uri != EMPTY_DEVICE_URI)
                {
                    _lastKnownName = _selectedVideoSource.Name;
                }
                if (_selectedVideoSource != null && viewLoaded)
                {
                    MediaElement.Close();
                }
                NotifyOfPropertyChange(() => SelectedVideoSource);
            }
        }

        public void resetDefaultVideoSeource(VideoSource videoSource)
        {
            _selectedVideoSource = videoSource;
            VideoReady = CamVideo.NOT_READY;
            if (viewLoaded)
            {
                MediaElement.Close();
            }
            updateVideoAndTelemetryStatuses();
            NotifyOfPropertyChange(() => SelectedVideoSource);
        }

        public Unosquare.FFME.MediaElement MediaElement
        {
            get
            {
                return m_MediaElement;
            }
        }

        private async Task startScreenStreaming()
        {
            _log.Debug("startScreenStreaming called");
            Debug.Assert(MediaElement != null, "MediaElement != null");
            Debug.Assert(!isNullOrEmpty(SelectedVideoSource), "!isNullOrEmpty(SelectedVideoSource)");
            if (MediaElement == null || isNullOrEmpty(SelectedVideoSource))
                return;

            if (MediaElement != null)
            {
                if (!await MediaElement.Open(SelectedVideoSource.Uri))
                {
                    _log.Info(string.Format("startScreenStreaming cant open stream on {0}", SelectedVideoSource.Name));
                    VideoMessage = string.Format(Resources.Failedtoloadvideofrom, SelectedVideoSource.Name);
                    VideoMessageVisibility = Visibility.Visible;
                    VideoReady = CamVideo.NOT_READY;
                    updateVideoAndTelemetryStatuses();
                }
            }
        }
        private void startMisp()
        {
            lock (_startStopLocker)
            {
                if (urtpServer == null)
                {
                    throw new Exception("urtpServer is null");
                }
                try
                {
                    MispStreamerParameters mispParams = new MispStreamerParameters()
                    {
                        TailNumber = Settings.Default.TailNumber,
                        TargetUri = urtpServer.OriginalString,
                        VehicleId = Settings.Default.InstallationId,
                    };
                    _mispStreamer = new MispVideoStreamer(mispParams);
                    _mispStreamer.StateChanged += onMispStreamerStateChanged;
                    _log.Debug("startMisp called");
                    _mispStreamer.Start();
                    _isStreaming = true;
                    _log.Debug("startMisp success");
                }
                catch (Exception e)
                {
                    _log.Info("startMisp error");
                    _log.Error(e);
                    throw;
                }

                _encoding = new EncodingWorker(getBitrateFromConfig());
                _encoding.Error += encoding_Error;
                _speedometer = new StreamWithBitrateControl(_mispStreamer.VideoStream, BPS_MEASURE_INTERVAL);
                _speedometer.PropertyChanged += speedControl_PropertyChanged;
                _encoding.Output = _speedometer;
            }
        }

        private static long? getBitrateFromConfig()
        {
            if (Settings.Default.BitrateAutomatic)
                return null;

            return Settings.Default.Bitrate * 1000 * 1000;
        }

        private void encoding_Error(object sender, Exception e)
        {
            _log.Error("Encoding error.", e);
            stopMisp();
            Execute.OnUIThreadAsync(() =>
            {
                MessageBox.Show(
                    App.Current.MainWindow,
                    "Unexpected error occured during encoding, streaming is stopped. Error: " + e.Message,
                    "Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            });
        }

        public void stopMisp(bool stopWithoutStateChange = false)
        {
            lock (_startStopLocker)
            {
                disposeEncoding();

                if (_mispStreamer == null)
                {
                    _log.Info("stopMisp called but mispStreamer is null");
                    return;
                }
                try
                {
                    if (stopWithoutStateChange)
                    {
                        _mispStreamer.StateChanged -= onMispStreamerStateChanged;
                    }
                    _log.Debug("stopMisp called");
                    _isStreaming = false;
                    _mispStreamer.Stop();
                    if (!stopWithoutStateChange)
                    {
                        _mispStreamer.StateChanged -= onMispStreamerStateChanged;
                    }
                    _mispStreamer.Dispose();
                    _mispStreamer = null;
                    _log.Debug("stopMisp success");
                }
                catch (Exception e)
                {
                    _log.Info("stopMisp error");
                    _log.Error(e);
                    throw;
                }
            }
        }

        public void StartStreaming()
        {
            _log.Debug("StartStreaming called");
            Task.Factory.StartNew(() =>
            {
                if (!_isStreaming)
                {
                    startMisp();
                    _log.Info(string.Format("new misp started {0}", urtpServer.OriginalString));
                }
                else
                {
                    stopMisp();
                }
                _hasConnected = false;
                updateVideoAndTelemetryStatuses();
            });
        }

        private void onMispStreamerStateChanged(object sender, EventArgs e)
        {
            new System.Threading.Thread((data) =>
            {
                MispVideoStreamer state = (MispVideoStreamer)sender;
                if (state != null)
                {
                    _log.Info(string.Format("misp new status {0}", state.State.ToString()));
                    switch (state.State)
                    {
                        case MispVideoStreamerState.NotStarted:
                            _videoStreamingStatus = VideoServerStatus.READY_TO_STREAM;
                            break;
                        case MispVideoStreamerState.Initial:
                            if (_videoStreamingStatus != VideoServerStatus.RECONNECTING)
                            {
                                _videoStreamingStatus = VideoServerStatus.INITIALIZING;
                            }
                            break;
                        case MispVideoStreamerState.Operational:
                            _videoStreamingStatus = VideoServerStatus.STREAMING;
                            _hasConnected = true;
                            break;
                        case MispVideoStreamerState.ConnectFailure:
                            _videoStreamingStatus = VideoServerStatus.CONNECTION_FAILED;
                            break;
                        case MispVideoStreamerState.ProtocolBadVersion:
                        case MispVideoStreamerState.OtherFailure:
                            _videoStreamingStatus = VideoServerStatus.FAILED;
                            break;
                        case MispVideoStreamerState.Finished:
                            _videoStreamingStatus = VideoServerStatus.FINISHED;
                            break;
                        default:
                            throw new Exception(string.Format("Unknown state submitted: {0}", state));
                    }
                    //ensure start stop in other thread.
                    if (_isStreaming &&
                            (state.State == MispVideoStreamerState.OtherFailure
                            || state.State == MispVideoStreamerState.ConnectFailure)
                            && _hasConnected
                            && _videoStreamingStatus != VideoServerStatus.RECONNECTING)
                    {
                        stopMisp(true);
                        if (urtpServer != null)
                        {
                            _videoStreamingStatus = VideoServerStatus.RECONNECTING;
                            startMisp();
                        }
                    }
                    else if (state.State == MispVideoStreamerState.ConnectFailure || state.State == MispVideoStreamerState.OtherFailure)
                    {
                        stopMisp(true);
                    }
                    updateVideoAndTelemetryStatuses();
                }
            }).Start();
        }

        private bool viewLoaded = false;
        public void ViewLoaded()
        {
            _log.Debug("ViewLoaded called");
            viewLoaded = true;
            m_MediaElement = (Application.Current.MainWindow as MainView)?.Media;
            MediaElement.VideoFrameDecoded += onVideoFrameDecoded;
            MediaElement.MediaInitializing += OnMediaInitializing;
            MediaElement.MediaOpening += OnMediaOpening;
            MediaElement.MediaOpened += OnMediaOpened;
            MediaElement.MediaClosed += onMediaClosed;
        }

        private void onMediaClosed(object sender, EventArgs e)
        {
            // No picture on the screen - no more encoder required because new picture may be in different size
            disposeEncoding();
        }

        private void disposeEncoding()
        {
            EncodingWorker encoding = _encoding;
            if (encoding != null)
            {
                encoding.Error -= encoding_Error;
                encoding.Dispose();
                _encoding = null;

                _speedometer?.Dispose();
                _speedometer = null;
                EncodingBitrate = new Bitrate();
            }
        }

        private unsafe void onVideoFrameDecoded(object sender, FrameDecodedEventArgs e)
        {
            var sw = Stopwatch.StartNew();
            if (_mispStreamer != null && _encoding != null && (_mispStreamer.State == MispVideoStreamerState.Initial || _mispStreamer.State == MispVideoStreamerState.Operational))
            {
                try
                {
                    Debug.WriteLine("Decoding");
                    _encoding.Feed(e.Frame);
                }
                catch (Exception err)
                {
                    _log.Error("Unexpected error occured during during encoding.", err);
                    stopMisp();

                    Execute.OnUIThreadAsync(() =>
                    {
                        MessageBox.Show(
                            App.Current.MainWindow,
                            "Unexpected error occured during encoding, streaming is stopped. Error: " + err.Message,
                            "Error",
                            MessageBoxButton.OK,
                            MessageBoxImage.Error);
                    });
                }
            }
            Debug.WriteLine(sw.Elapsed);
        }


        public void SettingsWindows()
        {
            _log.Debug("SettingsWindows called");
            _iWindowManager.ShowDialog(new SettingsViewModel(onSettingsSaved));
        }

        public void onSettingsSaved(HashSet<String> changed)
        {
            if (changed.Contains("UgcsAutomatic") || changed.Contains("UcgsAddress"))
            {
                if (_ucsConnectionService.IsConnected)
                {
                    _ucsConnectionService.Disconnect();
                }
            }

            if (changed.Contains("VideoServerAutomatic") || changed.Contains("VideoServerAddress"))
            {
                lock (_startStopLocker)
                {
                    urtpServer = null;
                }
                updateVideoAndTelemetryStatuses();

            }
            if (changed.Contains("BitrateAutomatic") || changed.Contains("Bitrate"))
            {
                //update bitrate here
                //Settings.Default.BitrateAutomatic;
                //Settings.Default.Bitrate
            }
            if (changed.Contains("HardwareDecodingEnable"))
            {
                MediaElement.Close();
            }
        }

        private void OnMediaOpening(object sender, MediaOpeningEventArgs e)
        {
            if (Settings.Default.HardwareDecodingEnable)
            {
                tryToEnableHardwareDecoding(e);
            }
            e.Options.MinimumPlaybackBufferPercent = 0;
            e.Options.DecoderParams.EnableFastDecoding = true;
            e.Options.DecoderParams.EnableLowDelayDecoding = true;
            e.Options.VideoBlockCache = 0;
            e.Options.IsTimeSyncDisabled = true;
            e.Options.IsAudioDisabled = true;

            Execute.OnUIThreadAsync(() =>
            {
                VideoMessage = Resources.Loadingvideo;
                VideoMessageVisibility = Visibility.Visible;
            });
            _log.Info("OnMediaOpening - CamVideo.NOT_READY");
            VideoReady = CamVideo.NOT_READY;
            updateVideoAndTelemetryStatuses();
        }

        private void tryToEnableHardwareDecoding(MediaOpeningEventArgs e)
        {
            var videoStream = e.Info.Streams.Where(x => x.Value.CodecType == AVMediaType.AVMEDIA_TYPE_VIDEO).FirstOrDefault();
            if (videoStream.Value.HardwareDecoders.Count > 0)
            {
                string hardwareDecoder = videoStream.Value.HardwareDecoders.First();
                _log.InfoFormat("Hardware decoders found: {0}. {1} will be used.",
                    String.Join("; ", e.Info.Streams[0].HardwareDecoders),
                    hardwareDecoder);
                e.Options.DecoderCodec.Add(videoStream.Key, hardwareDecoder);
            }
            else
            {
                _log.Info("No hardware decoders found.");
            }
        }

        private void OnMediaOpened(object sender, MediaOpenedEventArgs e)
        {
            Execute.OnUIThreadAsync(() =>
            {
                VideoMessage = string.Empty;
                VideoMessageVisibility = Visibility.Hidden;
            });
            _log.Info("OnMediaOpened - CamVideo.READY");
            VideoReady = CamVideo.READY;
            if (_mispStreamer != null && _isStreaming && _encoding == null)
            {
                _encoding = new EncodingWorker(getBitrateFromConfig());
                _encoding.Error += encoding_Error;
                _speedometer = new StreamWithBitrateControl(_mispStreamer.VideoStream, BPS_MEASURE_INTERVAL);
                _speedometer.PropertyChanged += speedControl_PropertyChanged;
                _encoding.Output = _speedometer;
            }
            updateVideoAndTelemetryStatuses();
        }

        private void speedControl_PropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(StreamWithBitrateControl.WriteSpeed))
            {
                EncodingBitrate = new Bitrate
                {
                    BitsPerSecond = ((StreamWithBitrateControl)sender).WriteSpeed * 8
                };
            }
        }

        private void OnMediaInitializing(object sender, MediaInitializingEventArgs e)
        {
            //      e.Configuration.GlobalOptions.EnableReducedBuffering = true;
            //     e.Configuration.GlobalOptions.FlagNoBuffer = true;

            // Ffme subscribes on ffmpeg log. To get log messages we should subscribe after ffme. Here is a good place.
            FfmpegLog.Enable(ffmpeg.AV_LOG_INFO);
            FfmpegLog.MessageReceived += FfmpegLog_MessageReceived;

            Execute.OnUIThreadAsync(() =>
            {
                VideoMessage = Resources.Loadingvideo;
                VideoMessageVisibility = Visibility.Visible;
            });
            _log.Info("OnMediaInitializing - CamVideo.NOT_READY");
            VideoReady = CamVideo.NOT_READY;
            updateVideoAndTelemetryStatuses();
        }

        private void FfmpegLog_MessageReceived(object sender, FfmpegLog.LogEventArgs e)
        {
            _log.Info(e.Message);
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

        private void updateVideoAndTelemetryStatuses()
        {
            NotifyOfPropertyChange(() => VideoServerStatus);
            NotifyOfPropertyChange(() => VideoServerStatusText);
            NotifyOfPropertyChange(() => TelemetryStatus);
            NotifyOfPropertyChange(() => TelemetryStatusText);
            NotifyOfPropertyChange(() => IsStreaming);
        }


        public TelemetryStatus TelemetryStatus
        {
            get
            {
                if (SelectedVehicle == null
                    || SelectedVehicle.VehicleId == EMPTY_VEHICLE_ID
                    || urtpServer == null
                    || !_ucsConnectionService.IsConnected
                    || !SelectedVehicle.IsConnected)
                {
                    return TelemetryStatus.NOT_READY_TO_STREAM;
                }
                else if (_videoStreamingStatus == VideoServerStatus.STREAMING)
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
                    || SelectedVideoSource.Uri == EMPTY_DEVICE_URI
                    || urtpServer == null
                    || VideoReady == CamVideo.NOT_READY)
                {
                    return VideoServerStatus.NOT_READY_TO_STREAM;
                }
                else if (_videoStreamingStatus == VideoServerStatus.RECONNECTING)
                {
                    return VideoServerStatus.RECONNECTING;
                }
                else if (_videoStreamingStatus == VideoServerStatus.INITIALIZING)
                {
                    return VideoServerStatus.INITIALIZING;
                }
                else if (_videoStreamingStatus == VideoServerStatus.FINISHED)
                {
                    return VideoServerStatus.FINISHED;
                }
                else if (_videoStreamingStatus == VideoServerStatus.STREAMING)
                {
                    return VideoServerStatus.STREAMING;
                }
                else if (_videoStreamingStatus == VideoServerStatus.FAILED)
                {
                    return VideoServerStatus.FAILED;
                }
                else if (_videoStreamingStatus == VideoServerStatus.CONNECTION_FAILED)
                {
                    return VideoServerStatus.CONNECTION_FAILED;
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
                    return Resources.VideoServernotdiscovered;
                }
                if (!_ucsConnectionService.IsConnected)
                {
                    return Resources.UgCSServerisnotconnected;
                }
                if (SelectedVehicle == null || SelectedVehicle.VehicleId == EMPTY_VEHICLE_ID)
                {
                    return Resources.Vehicleisnotselected;
                }
                if (!SelectedVehicle.IsConnected)
                {
                    return Resources.DroneOffline;
                }
                if (_videoStreamingStatus == VideoServerStatus.STREAMING)
                {
                    return string.Format(Resources.StreamingTo, urtpServer.Host + ":" + urtpServer.Port);
                }
                return string.Format(Resources.ReadytostreamtoVideoServer, urtpServer.Host + ":" + urtpServer.Port);
            }
        }
        public string VideoServerStatusText
        {
            get
            {
                if (urtpServer == null)
                {
                    return Resources.VideoServernotdiscovered;
                }
                if (SelectedVideoSource == null || SelectedVideoSource.Uri == EMPTY_DEVICE_URI)
                {
                    return Resources.Videosourceisnotselected;
                }
                if (VideoReady == CamVideo.NOT_READY)
                {
                    return Resources.Videosourceisnotstreamingvideo;
                }
                if (_videoStreamingStatus == VideoServerStatus.INITIALIZING)
                {
                    return Resources.Streaminitializing;
                }
                if (_videoStreamingStatus == VideoServerStatus.RECONNECTING)
                {
                    return Resources.ReconnectingtoVideoServer;
                }
                if (_videoStreamingStatus == VideoServerStatus.FAILED)
                {
                    return Resources.FailedtostartstreamtoVideoServer;
                }
                if (_videoStreamingStatus == VideoServerStatus.CONNECTION_FAILED)
                {
                    return Resources.ConnectionfailedtoVideoServer;
                }
                if (_isStreaming && _videoStreamingStatus == VideoServerStatus.STREAMING)
                {
                    return string.Format(Resources.StreamingTo, urtpServer.Host + ":" + urtpServer.Port);
                }
                return string.Format(Resources.ReadytostreamtoVideoServer, urtpServer.Host + ":" + urtpServer.Port);
            }
        }

        public bool IsStreaming
        {
            get
            {
                return _isStreaming;
            }
        }

        public string Title
        {
            get
            {
                return string.Format(Resources.UgCSVideoTransmitter,
                    Assembly.GetExecutingAssembly().GetName().Version.ToString());
            }
        }

    }
}

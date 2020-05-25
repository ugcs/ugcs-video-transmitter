using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reflection;
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
using Ugcs.Video.MispStreamer;
using Ugcs.Video.Tools;
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
        private const long BITRATE = 2 * 1024 * 1024;

        private log4net.ILog logger = log4net.LogManager.GetLogger(typeof(MainViewModel));

        private Timer telemetryTimer;

        private Timer ucsConnectionTimer;
        private Timer videoServerConnectionTimer;

        private Timer mediaTimer;

        private const string UCS_SERVER_TYPE = "ugcs:hci-server";

        private Uri urtpServer = null;
        public const string UGCS_VIDEOSERVER_URTP_ST = "ugcs:video-server:input:urtp";

        private VideoSourceDTO _defaultVideoDevice;
        private const string EMPTY_DEVICE_ID = "empty_device";

        private ClientVehicleDTO _defaultVehicle;
        private const int EMPTY_VEHICLE_ID = 0;

        private IDiscoveryService _discoveryService;
        private ConnectionService _ucsConnectionService;
        private VehicleListener _vehicleListener;
        private TelemetryListener _telemetryListener;
        private VehicleService _vehicleService;
        private VideoSourcesService _videoSourcesService;
        private Unosquare.FFME.MediaElement m_MediaElement;
        private IWindowManager _iWindowManager;
        private long _lastPacketRead = 0;
        private int _lastPacketReadTimeout = 5000;
        private MispVideoStreamer _mispStreamer;
        private VideoEncoder _encoder;
        private FrameRateCollector _frameRateCollector;

        public unsafe MainViewModel(DiscoveryService ds,
            ConnectionService cs,
            VehicleListener vl,
            TelemetryListener tl,
            VehicleService vs,
            VideoSourcesService vss,
            IWindowManager manager)
        {
            logger.Info("Application started");
            _iWindowManager = manager;
            _videoSourcesService = vss;
            _vehicleService = vs;
            _vehicleListener = vl;
            _telemetryListener = tl;
            _ucsConnectionService = cs;
            _discoveryService = ds;

            _discoveryService.AddToListen(UCS_SERVER_TYPE);
            _discoveryService.AddToListen(UGCS_VIDEOSERVER_URTP_ST);
            _discoveryService.ServiceLost += onSsdpServiceLost;


            _defaultVideoDevice = new VideoSourceDTO()
            {
                Name = Resources.Nodevice,
                Id = EMPTY_DEVICE_ID
            };
            VideoSourcesList.Add(_defaultVideoDevice);
            SelectedVideoSource = _defaultVideoDevice;
            
            _defaultVehicle = new ClientVehicleDTO()
            {
                Name = Resources.Novehicle,
                VehicleId = EMPTY_VEHICLE_ID
            };
            VehicleList.Add(_defaultVehicle);
            SelectedVehicle = _defaultVehicle;

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
            if (MediaElement != null && MediaElement.MediaState != MediaPlaybackState.Close)
            {
                if (DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - _lastPacketReadTimeout > _lastPacketRead && _lastPacketRead > 0)
                {
                    logger.Info("Media will close due packet timeout");
                    await MediaElement.Close();
                    _lastPacketRead = 0;
                    if (SelectedVideoSource != null && SelectedVideoSource.Id != EMPTY_DEVICE_ID)
                    {
                        VideoMessage = string.Format(Resources.Videostoppedfrom, SelectedVideoSource.Name);
                    }
                    else
                    {
                        VideoMessage = string.Format(Resources.Videostoppedfrom, _lastKnownName);
                    }
                    VideoMessageVisibility = Visibility.Visible;
                    VideoReady = CamVideo.NOT_READY;
                    updateVideoAndTelemetryStatuses();
                }
            }
            else if (MediaElement != null && MediaElement.MediaState == MediaPlaybackState.Close)
            {
                logger.Info("Try start new media");
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
                                updateVideoAndTelemetryStatuses();
                                logger.Info(string.Format("Found new videoserver {0}", urtpServer.AbsolutePath));
                            }
                            searhing = false;
                        });
                    }
                }
                else
                {
                    urtpServer = new Uri("urtp+connect://" + Settings.Default.VideoServerAddress + ":" + Settings.Default.VideoServerPort);
                    logger.Info(string.Format("Direct connection used to videoserver {0}", urtpServer.AbsolutePath));
                    updateVideoAndTelemetryStatuses();
                }
            }
        }
        private void OnTelemetryTimer(Object source, ElapsedEventArgs e)
        {
            if (SelectedVehicle != null && SelectedVehicle.VehicleId != EMPTY_VEHICLE_ID && _mispStreamer != null && _isStreaming)
            {
                var telemetry = _telemetryListener.GetTelemetryById(SelectedVehicle.VehicleId);
                if (telemetry != null)
                {
                    MispTelemetry tlm = new MispTelemetry()
                    {
                        Altitude = telemetry.AltitudeAMSL,
                        Longitude = telemetry.Longitude,
                        Latitude = telemetry.Latitude,
                        Heading = telemetry.Heading,
                        PlatformDesignation = SelectedVehicle.Name,
                        Pitch = telemetry.Pitch,
                        Roll = telemetry.Roll,
                        SensorRelativeAzimuth = telemetry.PayloadHeading,
                        SensorRelativeElevation = telemetry.PayloadPitch,
                        SensorRelativeRoll = telemetry.PayloadRoll
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
                logger.Error("UgCS connected");
            }
            catch (Exception e)
            {
                logger.Error("Could not connect to UgCS");
                logger.Error(e);
                ucsConnection_onDisconnected(null, null);
            }
        }
        private void ucsConnection_onDisconnected(object sender, EventArgs e)
        {
            Execute.OnUIThreadAsync(() =>
            {
                SelectedVehicle = _defaultVehicle;
                lock (vehicleUpdateLocked)
                {
                    foreach (var vInList in _vehicleList.Skip(1).ToList())
                    {
                        _vehicleList.Remove(vInList);
                    }
                }
                NotifyOfPropertyChange(() => VehicleList);
            });
            logger.Info("UgCS server disconnected");
            updateVideoAndTelemetryStatuses();
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
                            logger.Info(string.Format("Found new UgCS server {0}", location.OriginalString));
                            Connect(location);
                        }
                        searhing = false;
                    });
                }
            }
            else
            {
                var uri = new Uri("tcp://" + Settings.Default.UcgsAddress + ":" + Settings.Default.UcgsPort);
                logger.Info(string.Format("Direct connection used to UgCS server {0}", uri.OriginalString));
                Connect(uri);
            }
        }

        private void ucsConnection_onConnected(object sender, EventArgs args)
        {
            logger.Info("ucsConnection_onConnected called");
            var cs = sender as ConnectionService;
            updateVideoAndTelemetryStatuses();
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
            logger.Info("videoSources_onChanged called");
            List<VideoSourceDTO> sources = sender as List<VideoSourceDTO>;
            Execute.OnUIThreadAsync(() =>
            {
                bool mod = false;
                bool updateToDefault = false;
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
                    foreach (var source in _videoSourcesList.Skip(1).ToList())
                    {
                        if (!sources.Any(v => v.Name == source.Name))
                        {
                            if (SelectedVideoSource != null && SelectedVideoSource.Id == source.Id)
                            {
                                updateToDefault = true;
                            }
                            VideoSourcesList.Remove(source);
                            mod = true;
                        }
                    }
                }
                if (updateToDefault)
                {
                    SelectedVideoSource = _defaultVideoDevice;
                }
                if (mod)
                {
                    NotifyOfPropertyChange(() => VideoSourcesList);
                }
                var defaultVideo = VideoSourcesList.FirstOrDefault(v => v.Name == Settings.Default.LastCapureDevice);
                if (defaultVideo != null)
                {
                    SelectedVideoSource = defaultVideo;
                }
                else
                {
                    var defaultVideoLastKnown = VideoSourcesList.FirstOrDefault(v => v.Name == _lastKnownName);
                    if (defaultVideoLastKnown != null)
                    {
                        SelectedVideoSource = defaultVideoLastKnown;
                    }
                }
            });
        }

        private void onSsdpServiceLost(string serviceType, string location)
        {
            if (Settings.Default.VideoServerAutomatic == true 
                && serviceType == UGCS_VIDEOSERVER_URTP_ST 
                && location == urtpServer.OriginalString)
            {
                urtpServer = null;
                stopMisp();
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
            logger.Info(string.Format("Vehicle update call {0} {1}", vehicle.Name, modType.ToString()));
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
            logger.Info("Vehicle list update call");
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

                    foreach (var vInList in _vehicleList.Skip(1).ToList())
                    {
                        if (!list.Any(l => l == vInList.VehicleId))
                        {
                            _vehicleList.Remove(vInList);
                        }
                    }
                    if (_vehicleList.Count == 0)
                    {
                        SelectedVehicle = _defaultVehicle;
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
                if (_selectedVehicle != null && _selectedVehicle.VehicleId != EMPTY_VEHICLE_ID)
                {
                    Settings.Default.LastVehicleId = _selectedVehicle.VehicleId.ToString();
                    Settings.Default.Save();
                }
                NotifyOfPropertyChange(() => SelectedVehicle);
                if (_selectedVehicle != null && _selectedVehicle.VehicleId != EMPTY_VEHICLE_ID)
                {
                    logger.Info(string.Format("Vehicle selected {0}", _selectedVehicle.Name));
                }
                else
                {
                    logger.Info("Empty vehicle selected");
                }
                updateVideoAndTelemetryStatuses();
            }
        }

        private string _lastKnownName = string.Empty;
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
                if (_selectedVideoSource != null && _selectedVideoSource.Id != EMPTY_DEVICE_ID)
                {
                    Settings.Default.LastCapureDevice = _selectedVideoSource.Name;
                    Settings.Default.Save();
                }
                VideoReady = CamVideo.NOT_READY;
                updateVideoAndTelemetryStatuses();
                if (_selectedVideoSource != null && _selectedVideoSource.Id != EMPTY_DEVICE_ID)
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

        public Unosquare.FFME.MediaElement MediaElement
        {
            get
            {
                return m_MediaElement;
            }
        }

        private async Task StartScreenStreaming()
        {
            logger.Info("StartScreenStreaming called");
            if (MediaElement == null || SelectedVideoSource == null || SelectedVideoSource.Id == EMPTY_DEVICE_ID)
            {
                return;
            }
            if (MediaElement != null)
            {
                if (!await MediaElement.Open(new Uri($"device://dshow/?video={SelectedVideoSource.Name}")))
                {
                    logger.Info(string.Format("StartScreenStreaming cant open stream on {0}", SelectedVideoSource.Name));
                    VideoMessage = string.Format(Resources.Failedtoloadvideofrom, SelectedVideoSource.Name);
                    VideoMessageVisibility = Visibility.Visible;
                    VideoReady = CamVideo.NOT_READY;
                    updateVideoAndTelemetryStatuses();
                }
            }
        }
        private void startMisp()
        {
            if (_mispStreamer == null)
            {
                logger.Info("startMisp called but mispStreamer is null");
                return;
            }
            try
            {
                logger.Info("startMisp called");
                _mispStreamer.Start();
                _isStreaming = true;
                logger.Info("startMisp success");
            }
            catch (Exception e)
            {
                logger.Info("startMisp error");
                throw e;
            }
        }

        public void stopMisp()
        {
            if (_mispStreamer == null)
            {
                logger.Info("stopMisp called but mispStreamer is null");
                return;
            }
            try
            {
                logger.Info("stopMisp called");
                _isStreaming = false;
                _mispStreamer.Stop();
                logger.Info("stopMisp success");
            }
            catch (Exception e)
            {
                logger.Info("stopMisp error");
                throw e;
            }
        }

        private VideoServerStatus videoStreamingStatus = VideoServerStatus.NOT_READY_TO_STREAM;
        private bool _isStreaming = false;
        public void StartStreaming()
        {
            logger.Info("StartStreaming called");
            Task.Factory.StartNew(() =>
            {
                if (!_isStreaming)
                {
                    MispStreamerParameters mispParams = new MispStreamerParameters()
                    {
                        TailNumber = Settings.Default.TailNumber,
                        TargetUri = urtpServer.OriginalString,
                        VehicleId = Settings.Default.InstallationId,
                    };
                    _mispStreamer = new MispVideoStreamer(mispParams);
                    _mispStreamer.StateChanged += stateChanged;
                    startMisp();
                    logger.Info(string.Format("new misp started {0}", urtpServer.OriginalString));
                }
                else
                {
                    stopMisp();
                }
                hasConnected = false;
                updateVideoAndTelemetryStatuses();
            });
        }
        bool hasConnected = false;
        private void stateChanged(object sender, EventArgs e)
        {
            new System.Threading.Thread((data) =>
            {
                MispVideoStreamer state = (MispVideoStreamer)sender;
                if (state != null)
                {
                    logger.Info(string.Format("misp new status {0}", state.State.ToString()));
                    switch (state.State)
                    {
                        case MispVideoStreamerState.NotStarted:
                            videoStreamingStatus = VideoServerStatus.READY_TO_STREAM;
                            break;
                        case MispVideoStreamerState.Initial:
                            videoStreamingStatus = VideoServerStatus.INITIALIZING;
                            break;
                        case MispVideoStreamerState.Operational:
                            videoStreamingStatus = VideoServerStatus.STREAMING;
                            hasConnected = true;
                            break;
                        case MispVideoStreamerState.ConnectFailure:
                            videoStreamingStatus = VideoServerStatus.CONNECTION_FAILED;
                            break;
                        case MispVideoStreamerState.ProtocolBadVersion:
                        case MispVideoStreamerState.OtherFailure:
                            videoStreamingStatus = VideoServerStatus.FAILED;
                            break;
                        case MispVideoStreamerState.Finished:
                            videoStreamingStatus = VideoServerStatus.FAILED;
                            break;
                        default:
                            throw new Exception(string.Format("Unknown state submitted: {0}", state));
                    }
                    //ensure start stop in other thread.
                    if (_isStreaming &&
                            (videoStreamingStatus == VideoServerStatus.FAILED || videoStreamingStatus == VideoServerStatus.CONNECTION_FAILED) && hasConnected)
                    {
                        videoStreamingStatus = VideoServerStatus.RECONNECTING;
                        stopMisp();
                        startMisp();
                    }
                    updateVideoAndTelemetryStatuses();
                }
            });
        }

        private bool viewLoaded = false;
        public void ViewLoaded()
        {
            logger.Info("ViewLoaded called");
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
            _encoder?.Dispose();
            _encoder = null;
        }

        private unsafe void onVideoFrameDecoded(object sender, FrameDecodedEventArgs e)
        {
            if (MediaElement.MediaState == MediaPlaybackState.Play)
            {
                _lastPacketRead = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            }
            _frameRateCollector.FrameReceived();            

            if (_mispStreamer != null && (_mispStreamer.State == MispVideoStreamerState.Initial || _mispStreamer.State == MispVideoStreamerState.Operational))
            {
                if (_encoder == null)
                {
                    // We don't know the image size and frame rate before the first frame is decoded, 
                    // this is why here is a good place to initialize encoder

                    if (!_frameRateCollector.FrameRate.HasValue)
                        return;

                    try
                    {
                        _encoder = new VideoEncoder(
                            e.Frame->width,
                            e.Frame->height,
                            AVPixelFormat.AV_PIX_FMT_YUV422P,
                            BITRATE,
                            _frameRateCollector.FrameRate.Value);
                    }
                    catch (Exception err)
                    {
                        // TODO: Log error 
                        // <here>

                        stopMisp();
                        Execute.OnUIThreadAsync(() =>
                        {
                            MessageBox.Show(
                                App.Current.MainWindow,
                                "Encoder initialization error: " + err.Message,
                                "Error",
                                MessageBoxButton.OK,
                                MessageBoxImage.Error);
                        });
                    }
                }

                try
                {
                    _encoder.Encode(e.Frame, _mispStreamer.VideoStream);
                }
                catch (ObjectDisposedException err)
                {
                    // Stream is finished and object is disposed.
                    // TODO: Log verbose.
                }
                catch (InvalidOperationException err)
                {
                    // Looks lile streamer was closed. It's ok to do nothing
                    // TODO: Log verbose
                }
                catch (Exception err)
                {
                    // TODO: Log error
                }
            }
        }


        public void SettingsWindows()
        {
            logger.Info("SettingsWindows called");
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
                urtpServer = null;
                updateVideoAndTelemetryStatuses();

            }
        }

        private void OnMediaOpening(object sender, MediaOpeningEventArgs e)
        {
            Execute.OnUIThreadAsync(() =>
            {
                VideoMessage = Resources.Loadingvideo;
                VideoMessageVisibility = Visibility.Visible;
            });
            logger.Info("OnMediaOpening - CamVideo.NOT_READY");
            VideoReady = CamVideo.NOT_READY;
            updateVideoAndTelemetryStatuses();
            _frameRateCollector = new FrameRateCollector(15);
        }
        private void OnMediaOpened(object sender, MediaOpenedEventArgs e)
        {
            Execute.OnUIThreadAsync(() =>
            {
                VideoMessage = string.Empty;
                VideoMessageVisibility = Visibility.Hidden;
            });
            logger.Info("OnMediaOpened - CamVideo.READY");
            VideoReady = CamVideo.READY;
            updateVideoAndTelemetryStatuses();
        }
        private void OnMediaInitializing(object sender, MediaInitializingEventArgs e)
        {
            // Ffme subscribes on ffmpeg log. To get log messages we should subscribe after ffme. Here is a good place.
            FfmpegLog.Enable(ffmpeg.AV_LOG_WARNING);

            Execute.OnUIThreadAsync(() =>
            {
                VideoMessage = Resources.Loadingvideo;
                VideoMessageVisibility = Visibility.Visible;
            });
            logger.Info("OnMediaInitializing - CamVideo.NOT_READY");
            VideoReady = CamVideo.NOT_READY;
            updateVideoAndTelemetryStatuses();
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
                    || SelectedVideoSource == null
                    || SelectedVideoSource.Id == EMPTY_DEVICE_ID
                    || urtpServer == null
                    || !_ucsConnectionService.IsConnected)
                {
                    return TelemetryStatus.NOT_READY_TO_STREAM;
                }
                else if (_isStreaming)
                {
                    if (videoStreamingStatus == VideoServerStatus.INITIALIZING)
                    {
                        return TelemetryStatus.READY_TO_STREAM;
                    }
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
                    || SelectedVideoSource.Id == EMPTY_DEVICE_ID
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
                else if (videoStreamingStatus == VideoServerStatus.STREAMING)
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
                    return Resources.VideoServernotdiscovered;
                }
                if (!_ucsConnectionService.IsConnected)
                {
                    return Resources.UgCSServerisnotconnected;
                }
                if (SelectedVideoSource == null || SelectedVideoSource.Id == EMPTY_DEVICE_ID)
                {
                    return Resources.Videosourcenotselected;
                }
                if (SelectedVehicle == null || SelectedVehicle.VehicleId == EMPTY_VEHICLE_ID)
                {
                    return Resources.Vehicleisnotselected;
                }
                if (_isStreaming)
                {
                    return Resources.Streaming;
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
                if (SelectedVideoSource == null || SelectedVideoSource.Id == EMPTY_DEVICE_ID)
                {
                    return Resources.Videosourceisnotselected;
                }
                if (VideoReady == CamVideo.NOT_READY)
                {
                    return Resources.Videosourceisnotstreamingvideo;
                }
                if (videoStreamingStatus == VideoServerStatus.INITIALIZING)
                {
                    return Resources.Streaminitializing;
                }
                if (_isStreaming)
                {
                    if (videoStreamingStatus == VideoServerStatus.INITIALIZING)
                    {
                        return string.Format(Resources.ReadytostreamtoVideoServer, urtpServer.Host + ":" + urtpServer.Port);
                    }
                    return Resources.Streaming;
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

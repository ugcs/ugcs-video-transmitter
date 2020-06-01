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
        private readonly long? BITRATE = null; // null - auto, example: 1 * 1024 * 1024;

        private log4net.ILog _log = log4net.LogManager.GetLogger(typeof(MainViewModel));

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
        private MispVideoStreamer _mispStreamer;
        private EncodingWorker _encoding;

        //VideoServer internal statuses
        private VideoServerStatus _videoStreamingStatus = VideoServerStatus.NOT_READY_TO_STREAM;
        private bool _isStreaming;
        private bool _hasConnected = false;
        private object _startStopLocker = new object();

        public unsafe MainViewModel(DiscoveryService ds,
            ConnectionService cs,
            VehicleListener vl,
            TelemetryListener tl,
            VehicleService vs,
            VideoSourcesService vss,
            IWindowManager manager)
        {
            _log.Info("Application started");
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
            resetDefaultVideoSeource(_defaultVideoDevice);
            
            _defaultVehicle = new ClientVehicleDTO()
            {
                Name = Resources.Novehicle,
                VehicleId = EMPTY_VEHICLE_ID
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
            if (MediaElement != null && MediaElement.MediaState == MediaPlaybackState.Close)
            {
                _log.Info("Try start new media");
                await startScreenStreaming();
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
                                lock (_startStopLocker)
                                {
                                    urtpServer = location;
                                }
                                updateVideoAndTelemetryStatuses();
                                _log.Info(string.Format("Found new videoserver {0}", urtpServer.AbsolutePath));
                            }
                            searhing = false;
                        });
                    }
                }
                else
                {
                    lock (_startStopLocker)
                    {
                        urtpServer = new Uri("urtp+connect://" + Settings.Default.VideoServerAddress + ":" + Settings.Default.VideoServerPort);
                    }
                    _log.Info(string.Format("Direct connection used to videoserver {0}", urtpServer.AbsolutePath));
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
            _log.Info("UgCS server disconnected");
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
                            _log.Info(string.Format("Found new UgCS server {0}", location.OriginalString));
                            Connect(location);
                        }
                        searhing = false;
                    });
                }
            }
            else
            {
                var uri = new Uri("tcp://" + Settings.Default.UcgsAddress + ":" + Settings.Default.UcgsPort);
                _log.Info(string.Format("Direct connection used to UgCS server {0}", uri.OriginalString));
                Connect(uri);
            }
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
                    _telemetryListener.SubscribeTelemtry();
                });
            });
        }
        private void videoSources_onChanged(object sender, EventArgs e)
        {
            _log.Debug("videoSources_onChanged called");
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
                    resetDefaultVideoSeource(_defaultVideoDevice);
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
            _log.Info(string.Format("Vehicle update call {0} {1}", vehicle.Name, modType.ToString()));
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
                if (_selectedVehicle != null)
                {
                    Settings.Default.LastVehicleId = _selectedVehicle.VehicleId.ToString();
                    Settings.Default.Save();
                }
                NotifyOfPropertyChange(() => SelectedVehicle);
                if (_selectedVehicle != null && _selectedVehicle.VehicleId != EMPTY_VEHICLE_ID)
                {
                    _log.Info(string.Format("Vehicle selected {0}", _selectedVehicle.Name));
                }
                else
                {
                    _log.Info("Empty vehicle selected");
                }
                updateVideoAndTelemetryStatuses();
            }
        }

        public void resetDefaultSelectedVehicle(ClientVehicleDTO videoSource)
        {
            _selectedVehicle = videoSource;
            updateVideoAndTelemetryStatuses();
            NotifyOfPropertyChange(() => SelectedVehicle);
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
                if (_selectedVideoSource != null)
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

        public void resetDefaultVideoSeource(VideoSourceDTO videoSource)
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
            if (MediaElement == null || SelectedVideoSource == null || SelectedVideoSource.Id == EMPTY_DEVICE_ID)
            {
                return;
            }
            if (MediaElement != null)
            {
                if (!await MediaElement.Open(new Uri($"device://dshow/?video={SelectedVideoSource.Name}")))
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

                _encoding = new EncodingWorker(null);
                _encoding.Output = _mispStreamer.VideoStream;
                _encoding.Error += encoding_Error;
            }
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
        }

        private void OnMediaOpening(object sender, MediaOpeningEventArgs e)
        {
            e.Options.MinimumPlaybackBufferPercent = 0;
            e.Options.DecoderParams.EnableFastDecoding = true;
            e.Options.DecoderParams.EnableLowDelayDecoding = true;
            e.Options.VideoBlockCache = 0;
            e.Options.IsTimeSyncDisabled = true;


            Execute.OnUIThreadAsync(() =>
            {
                VideoMessage = Resources.Loadingvideo;
                VideoMessageVisibility = Visibility.Visible;
            });
            _log.Info("OnMediaOpening - CamVideo.NOT_READY");
            VideoReady = CamVideo.NOT_READY;
            updateVideoAndTelemetryStatuses();
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
                _encoding = new EncodingWorker(null);
                _encoding.Error += encoding_Error;
                _encoding.Output = _mispStreamer.VideoStream;
            }
            updateVideoAndTelemetryStatuses();
        }
        private void OnMediaInitializing(object sender, MediaInitializingEventArgs e)
        {
            e.Configuration.GlobalOptions.EnableReducedBuffering = true;
            e.Configuration.GlobalOptions.FlagNoBuffer = true;

            // Ffme subscribes on ffmpeg log. To get log messages we should subscribe after ffme. Here is a good place.
            FfmpegLog.Enable(ffmpeg.AV_LOG_WARNING);

            Execute.OnUIThreadAsync(() =>
            {
                VideoMessage = Resources.Loadingvideo;
                VideoMessageVisibility = Visibility.Visible;
            });
            _log.Info("OnMediaInitializing - CamVideo.NOT_READY");
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
                    || urtpServer == null
                    || !_ucsConnectionService.IsConnected)
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
                    || SelectedVideoSource.Id == EMPTY_DEVICE_ID
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
                if (SelectedVideoSource == null || SelectedVideoSource.Id == EMPTY_DEVICE_ID)
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

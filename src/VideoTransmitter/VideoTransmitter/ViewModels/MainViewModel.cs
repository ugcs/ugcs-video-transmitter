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
using VideoSources;
using VideoSources.DTO;
using VideoTransmitter.Properties;
using VideoTransmitter.Views;


namespace VideoTransmitter.ViewModels
{
    public partial class MainViewModel : Caliburn.Micro.PropertyChangedBase
    {
        private Timer telemetryTimer;

        private Timer ucsConnectionTimer;
        private Timer videoServerConnectionTimer;

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
                VideoSourcesList = new ObservableCollection<VideoSourceDTO>(_videoSourcesService.GetVideoSources());
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
                                VideoServerConnection = urtpServer.Host + ":" + urtpServer.Port;
                            }
                            searhing = false;
                        });
                    }
                }
                else
                {
                    urtpServer = new Uri("urtp+connect://" + Settings.Default.VideoServerAddress + ":" + Settings.Default.VideoServerPort);
                    VideoServerConnection = Settings.Default.VideoServerAddress + ":" + Settings.Default.VideoServerPort;
                }
            }
        }
        private void OnTelemetryTimer(Object source, ElapsedEventArgs e)
        {
            if (SelectedVehicle != null)
            {
                var telemetry = _telemetryListener.GetTelemetryById(SelectedVehicle.VehicleId);
                //TODO: send misp telemetry
            }
        }

        public void Connect(Uri address)
        {
            try
            {
                _ucsConnectionService.Connect(address, new UcsCredentials(string.Empty, string.Empty));
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
            }
        }

        private string videoServerConnection = "Not found";
        public string VideoServerConnection
        {
            get
            {
                return videoServerConnection;
            }
            set
            {
                videoServerConnection = value;
                NotifyOfPropertyChange(() => VideoServerConnection);
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
                                //   _telemetryListener.AddVehicleIdTolistener(vehicle.Id, TelemetryCallBack);
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
                    bool setActiveVehicle = false;
                    if (_vehicleList.Count == 0)
                    {
                        setActiveVehicle = true;
                    }
                    var list = new List<int>();
                    foreach (var vInList in vehicleList)
                    {
                        if (!_vehicleList.Any(v => v.VehicleId == vInList.VehicleId))
                        {
                            _vehicleList.Add(vInList);
                            //_telemetryListener.AddVehicleIdTolistener(vInList.Id, TelemetryCallBack);
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
                NotifyOfPropertyChange(() => SelectedVehicle);
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
                if (_selectedVideoSource != null && value != null && _selectedVehicle.Name == value.Name)
                {
                    return;
                }
                _selectedVideoSource = value;
                NotifyOfPropertyChange(() => SelectedVideoSource);
            }
        }

        public Unosquare.FFME.MediaElement MediaElement
        {
            get
            {
                if (m_MediaElement == null)
                    m_MediaElement = (Application.Current.MainWindow as MainView)?.Media;

                return m_MediaElement;
            }
        }

        public async void StartStreaming()
        {
            if (MediaElement == null)
            {
                MessageBox.Show("Media element is null");
                return;
            }
            if (SelectedVideoSource == null)
            {
                MessageBox.Show("Please select video source");
                return;
            }
            if (MediaElement != null)
            {
                if (!await MediaElement.Open(new Uri($"device://dshow/?video={SelectedVideoSource.Name}")))
                {
                    MessageBox.Show($"Can't open video from {SelectedVideoSource.Name}.");
                }
            }
        }

        public void ViewLoaded()
        {
            MediaElement.PacketRead += packedRead;
        }
        private unsafe void packedRead(object sender, Unosquare.FFME.Common.PacketReadEventArgs e)
        {
            // Capture stream here
            Debug.WriteLine(e.Packet->size);
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
                VideoServerConnection = "Not found";
            }
        }

    }
}

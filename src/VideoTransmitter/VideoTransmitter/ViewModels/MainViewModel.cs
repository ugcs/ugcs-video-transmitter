using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using Caliburn.Micro;
using SSDPDiscoveryService;
using UcsService;
using UcsService.DTO;
using UcsService.Enums;

namespace VideoTransmitter.ViewModels
{
    public partial class MainViewModel : Caliburn.Micro.PropertyChangedBase
    {
        private const string UCS_SERVER_TYPE = "ugcs:hci-server";
        private Uri urtpServer = null;
        private Uri udpServer = null;

        public const string UGCS_VIDEOSERVER_UDP_ST = "ugcs:video-server:input:udp";
        public const string UGCS_VIDEOSERVER_URTP_ST = "ugcs:video-server:input:urtp";
        private IDiscoveryService discoveryService;
        private ConnectionService ucsConnectionService;
        private VehicleListener _vehicleListener;
        private VehicleService _vehicleService;
        public MainViewModel(DiscoveryService ds, ConnectionService cs, VehicleListener vl, VehicleService vs)
        {
            _vehicleService = vs;
            _vehicleListener = vl;
            ucsConnectionService = cs;
            discoveryService = ds;

            ucsConnectionService.Connected += ucsConnection_onConnected;
            ucsConnectionService.Disconnected += ucsConnection_onDisconnected;
            discoveryService.StartListen();

            Task.Run(() => startDiscoveringUcsAsync(onUcsServiceDiscovered));
            Task.Run(() => startDiscoveringURTPVideoserverAsync(onVideoServiceDiscovered));
        }
        private void onVideoServiceDiscovered(Uri videoServer, string viseoserverSt)
        {
            switch (viseoserverSt)
            {
                case UGCS_VIDEOSERVER_UDP_ST:
                    udpServer = videoServer;
                    break;
                case UGCS_VIDEOSERVER_URTP_ST:
                    urtpServer = videoServer;
                    break;
            }
            if (udpServer != null || urtpServer != null)
            {
                //TODO: selector between URT & UDP
                VideoServerConnection = videoServer.Host + ":" + videoServer.Port;
            }
        }
        private void onUcsServiceDiscovered(Uri ucsService)
        {
            ConnectAsync(ucsService);
        }
        private async Task startDiscoveringUcsAsync(Action<Uri> onDiscovered)
        {
            Uri videoServerUrl = await discoveryService.TryFoundAsync(UCS_SERVER_TYPE);
            onDiscovered(videoServerUrl);
        }
        private async Task startDiscoveringURTPVideoserverAsync(Action<Uri, string> onDiscovered)
        {
            Uri videoServerUrl = await discoveryService.TryFoundAsync(UGCS_VIDEOSERVER_UDP_ST);
            onDiscovered(videoServerUrl, UGCS_VIDEOSERVER_UDP_ST);
        }
        private async Task startDiscoveringUDPVideoserverAsync(Action<Uri, string> onDiscovered)
        {
            Uri videoServerUrl = await discoveryService.TryFoundAsync(UGCS_VIDEOSERVER_UDP_ST);
            onDiscovered(videoServerUrl, UGCS_VIDEOSERVER_UDP_ST);
        }

        public void ConnectAsync(Uri address)
        {
            Task.Run(() => ucsConnectionService.ConnectAsync(address, new UcsCredentials(string.Empty, string.Empty)));
        }
        private void ucsConnection_onDisconnected(object sender, EventArgs e)
        {
            UcsConnection = "Disconnected";
            Task.Run(() => startDiscoveringUcsAsync(onUcsServiceDiscovered));
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
                });
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

        private string videoServerConnection = "Not Found";
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
                    if (_vehicleList.Count > 0 && setActiveVehicle && SelectedVehicle == null)
                    {
                        SelectedVehicle = _vehicleList.First();
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


    }
}

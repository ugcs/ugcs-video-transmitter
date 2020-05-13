using System;
using System.Threading.Tasks;
using System.Windows;
using SSDPDiscoveryService;
using UcsService;

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
        public MainViewModel(DiscoveryService ds, ConnectionService cs)
        {
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
    }
}

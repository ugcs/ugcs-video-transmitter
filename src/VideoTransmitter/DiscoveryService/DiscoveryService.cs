using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using UGCS.Ssdp;

namespace SSDPDiscoveryService
{
    public class DiscoveryService : IDiscoveryService, IDisposable
    {

        SsdpAgent _ssdpAgent;

        private const string DEFAULT_MULTICAST_IP = "239.198.46.46";
        private const int DEFAULT_PORT = 1991;
        
        private log4net.ILog _log = log4net.LogManager.GetLogger(typeof(DiscoveryService));

        // key - type of service that is listened for
        // value - list of listeners
        private readonly ConcurrentDictionary<string, IList<ServiceUrlEventHandler>> _listeners = new ConcurrentDictionary<string, IList<ServiceUrlEventHandler>>();
        
        public DiscoveryService()
            : this(DEFAULT_MULTICAST_IP, DEFAULT_PORT)
        {

        }

        public DiscoveryService(string multicastIp, int port)
        {
            if (port < 1 || port > 65355)
                throw new ArgumentOutOfRangeException(nameof(port), "Value must be between 1 and 65355");

            IPAddress ip = IPAddress.Parse(multicastIp);

            _ssdpAgent = new SsdpAgent(ip, port);
        }

        public void StartListen()
        {
            _ssdpAgent.Start();
            _ssdpAgent.NewStatus += ssdpEventHandler;
        }

        public async Task<Uri> TryFoundAsync(string serviceType, TimeSpan? timeout = null)
        {
            if (timeout.HasValue && timeout.Value <= TimeSpan.Zero)
                throw new ArgumentException("Value must be greater then zero.", nameof(timeout));

            Uri uri = null;
            SemaphoreSlim semaphore = new SemaphoreSlim(0, 1);
            TryFound(serviceType, (serviceUri) =>
            {
                uri = serviceUri;
                semaphore.Release();
            });
            if(timeout.HasValue)
                await semaphore.WaitAsync(timeout.Value);
            else
                await semaphore.WaitAsync();
            return uri;
        }

        public void TryFound(string serviceType, ServiceUrlEventHandler onFound)
        {
            if (String.IsNullOrEmpty(serviceType))
                throw new ArgumentException(String.Format("{0}: must be not null and not empty", nameof(serviceType)));
            if (onFound == null)
                throw new ArgumentNullException(nameof(onFound));

            if(_listeners.TryGetValue(serviceType, out var serviceUrlEventHandlers))
            {
                if (!serviceUrlEventHandlers.Contains(onFound))
                    serviceUrlEventHandlers.Add(onFound);
                else
                {
                    InvalidOperationException e = new InvalidOperationException("Same listener can not be added for one service twice");
                    _log.Error("Error:", e);
                    throw e;
                }
            }
            else
            {
                List<ServiceUrlEventHandler> newListeners = new List<ServiceUrlEventHandler>();
                newListeners.Add(onFound);
                _listeners.TryAdd(serviceType, newListeners);
                _ssdpAgent.SubscribeService(serviceType);
            }
        }

        private void ssdpEventHandler(object sender, SsdpServiceStatusEventArgs SsdpArgs)
        {
            if (!SsdpArgs.IsAlive)//ignore services which notify they die
                return;

            _log.DebugFormat("Srvice with type \"{0}\" found at '{1}'.", SsdpArgs.ServiceInfo.Type, SsdpArgs.ServiceInfo.Location);
            try
            {
                string serviceType = SsdpArgs.ServiceInfo.Type;
                Uri locationUri = new Uri(SsdpArgs.ServiceInfo.Location);
                if (_listeners.TryRemove(serviceType, out var listeners))
                {
                    foreach(ServiceUrlEventHandler listener in listeners)
                    {
                        listener.Invoke(locationUri);
                    }
                }
            }
            catch (UriFormatException)
            {
                _log.DebugFormat("Invalid uri received [uri: '{0}']. Message ignored.", SsdpArgs.ServiceInfo.Location);
            }
        }

        public void StopListen()
        {
            _ssdpAgent.Stop();
        }

        public void Dispose()
        {
            StopListen();
            _ssdpAgent.Dispose();
        }
    }
}

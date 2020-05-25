using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Timers;
using UGCS.Ssdp;

namespace SSDPDiscoveryService
{
    public delegate void ServiceLost(string serviceType, string location);
    public class DiscoveryService : IDiscoveryService, IDisposable
    {
        SsdpAgent _ssdpAgent;

        private const string DEFAULT_MULTICAST_IP = "239.198.46.46";
        private const int DEFAULT_PORT = 1991;
        
        private log4net.ILog _log = log4net.LogManager.GetLogger(typeof(DiscoveryService));

        private object _locker = new object();
        private const int TIMEOUT_SEC = 30;
        private List<string> servicesToFind = new List<string>();
        private readonly Dictionary<string, ConcurrentDictionary<string, long>> foundServices = new Dictionary<string, ConcurrentDictionary<string, long>>();

        private const int DEAD_SSDP_CHECK_INTERVAL = 500;
        private System.Timers.Timer serviceTimer;

        // key - type of service that is listened for
        // value - list of listeners
        private readonly ConcurrentDictionary<string, IList<ServiceUrlEventHandler>> _listeners = new ConcurrentDictionary<string, IList<ServiceUrlEventHandler>>();

        public DiscoveryService()
            : this(DEFAULT_MULTICAST_IP, DEFAULT_PORT)
        {
            serviceTimer = new System.Timers.Timer(DEAD_SSDP_CHECK_INTERVAL);
            serviceTimer.Elapsed += onServiceTimer;
            serviceTimer.AutoReset = true;
            serviceTimer.Enabled = true;
        }

        public DiscoveryService(string multicastIp, int port)
        {
            if (port < 1 || port > 65355)
                throw new ArgumentOutOfRangeException(nameof(port), "Value must be between 1 and 65355");

            IPAddress ip = IPAddress.Parse(multicastIp);

            _ssdpAgent = new SsdpAgent(ip, port);
        }

        public event ServiceLost ServiceLost;

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

            _log.DebugFormat("Service with type \"{0}\" found at '{1}'.", SsdpArgs.ServiceInfo.Type, SsdpArgs.ServiceInfo.Location);
            try
            {
                lock (_locker)
                {
                    if (foundServices.ContainsKey(SsdpArgs.ServiceInfo.Type))
                    {
                        foundServices[SsdpArgs.ServiceInfo.Type].AddOrUpdate(SsdpArgs.ServiceInfo.Location,
                            DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                            (k, old) => DateTimeOffset.UtcNow.ToUnixTimeSeconds());
                    }
                }
                
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

        public void AddToListen(string serviceType)
        {
            lock (_locker)
            {
                if (!foundServices.ContainsKey(serviceType))
                {
                    foundServices.Add(serviceType, new ConcurrentDictionary<string, long>());
                }
            }
        }

        public string GetService(string serviceType)
        {
            lock (_locker)
            {
                if (foundServices.ContainsKey(serviceType))
                {
                    return foundServices[serviceType].Keys.FirstOrDefault();
                }
                return null;
            }
        }

        private void onServiceTimer(object source, ElapsedEventArgs e)
        {
            List<Tuple<string, string>> removed = new List<Tuple<string, string>>();
            lock (_locker)
            {
                Parallel.ForEach(foundServices, (service) => {
                    foreach (var location in service.Value)
                    {
                        if (location.Value < DateTimeOffset.UtcNow.ToUnixTimeSeconds() - TIMEOUT_SEC)
                        {
                            if (service.Value.TryRemove(location.Key, out var val))
                            {
                                removed.Add(new Tuple<string, string>(service.Key, location.Key));
                            }
                        }
                    }
                });
            }
            if (removed.Count > 0)
            {
                foreach(var removedUrl in removed)
                {
                    ServiceLost?.Invoke(removedUrl.Item1, removedUrl.Item2);
                }
            }
        }
    }
}

using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

namespace SSDPDiscoveryService
{
    public interface IDiscoveryService
    {
        /// <summary>
        /// Try to find out url of service with specified type.
        /// </summary>
        /// <param name="serviceType"></param>
        /// <param name="timeout"></param>
        /// <returns>Return null if timeout is over.</returns>
        Task<Uri> TryFoundAsync(string serviceType, TimeSpan? timeout = null);

        /// <summary>
        /// Call <paramref name="receiveUrlHandler"/> on service found. Call once.
        /// </summary>
        /// <param name="serviceType">Type of service that should be found</param>
        /// <param name="receiveUrlHandler">Handler for found service url</param>
        void TryFound(string serviceType, ServiceUrlEventHandler receiveUrlHandler);

        /// <summary>
        /// Start discovering net for services.
        /// </summary>
        void StartListen();

        /// <summary>
        /// Stop discovering net for services
        /// </summary>
        void StopListen();
    }
}

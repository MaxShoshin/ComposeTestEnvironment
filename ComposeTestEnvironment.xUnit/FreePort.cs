using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;

namespace ComposeTestEnvironment.xUnit
{
    internal static class FreePort
    {
        public const ushort DynamicPortStart = 50560;

        public static IReadOnlyList<ushort> Rent(in int portCount)
        {
            return Rent(DynamicPortStart, portCount);
        }

        public static IReadOnlyList<ushort> Rent(ushort startingPort, in int portCount)
        {
            var connections = Array.Empty<TcpConnectionInformation>();
            var tcpEndPoints = Array.Empty<IPEndPoint>();
            var udpEndPoints = Array.Empty<IPEndPoint>();
            try
            {
                var properties = IPGlobalProperties.GetIPGlobalProperties();

                // Ignore active connections
                connections = properties.GetActiveTcpConnections();
                // Ignore active tcp listners
                tcpEndPoints = properties.GetActiveTcpListeners();
                // Ignore active UDP listeners
                udpEndPoints = properties.GetActiveUdpListeners();
            }
            catch (PlatformNotSupportedException)
            {
            }

            var busyPorts = new HashSet<int>();

            busyPorts.UnionWith(connections
                                    .Where(connection => connection.LocalEndPoint.Port >= startingPort)
                                    .Select(connection => connection.LocalEndPoint.Port));

            busyPorts.UnionWith(tcpEndPoints.Where(n => n.Port >= startingPort).Select(endPoint => endPoint.Port));


            busyPorts.UnionWith(udpEndPoints.Where(n => n.Port >= startingPort).Select(endPoint => endPoint.Port));

            var availablePorts = new List<ushort>(portCount);
            for (var port = startingPort; port < ushort.MaxValue; port++)
            {
                if (!busyPorts.Contains(port))
                {
                    availablePorts.Add(port);

                    if (availablePorts.Count == portCount)
                    {
                        return availablePorts;
                    }
                }
            }

            throw new InvalidOperationException(
                $"Not enough free ports (try find {portCount} free port starting form {startingPort})");
        }
    }
}

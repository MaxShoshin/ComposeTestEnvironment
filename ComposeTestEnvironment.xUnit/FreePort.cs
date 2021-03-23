using System;
using System.Collections.Generic;
using System.Linq;
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
            var properties = IPGlobalProperties.GetIPGlobalProperties();

            // Ignore active connections
            var connections = properties.GetActiveTcpConnections();

            var busyPorts = new HashSet<int>();

            busyPorts.UnionWith(connections
                                    .Where(connection => connection.LocalEndPoint.Port >= startingPort)
                                    .Select(connection => connection.LocalEndPoint.Port));

            // Ignore active tcp listners
            var endPoints = properties.GetActiveTcpListeners();
            busyPorts.UnionWith(endPoints.Where(n => n.Port >= startingPort).Select(endPoint => endPoint.Port));

            // Ignore active UDP listeners
            endPoints = properties.GetActiveUdpListeners();
            busyPorts.UnionWith(endPoints.Where(n => n.Port >= startingPort).Select(endPoint => endPoint.Port));

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

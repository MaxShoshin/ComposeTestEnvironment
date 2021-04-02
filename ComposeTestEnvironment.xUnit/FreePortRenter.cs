using System.Collections.Generic;
using System.Linq;

namespace ComposeTestEnvironment.xUnit
{
    public sealed class FreePortRenter : IPortRenter
    {
        private ushort _lastPort;

        public FreePortRenter(ushort startPort)
        {
            _lastPort = startPort;
        }

        public IReadOnlyList<ushort> Rent(string serviceServiceName, int portCount)
        {
            var ports = FreePort.Rent(_lastPort, portCount);

            _lastPort = (ushort)(ports.Max() + 1);

            return ports;
        }
    }
}

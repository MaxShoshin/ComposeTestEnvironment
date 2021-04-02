using System.Collections.Generic;
using System.Linq;

namespace ComposeTestEnvironment.xUnit
{
    public sealed class SerialPortRenter : IPortRenter
    {
        private ushort _lastPort;

        public SerialPortRenter(ushort startPort)
        {
            _lastPort = startPort;
        }

        public IReadOnlyList<ushort> Rent(string serviceServiceName, int portCount)
        {
            var ports = Enumerable.Range(_lastPort, portCount);

            _lastPort = (ushort)(ports.Max() + 1);

            return ports.Select(x => (ushort)x).ToList();
        }
    }
}

using System.Collections.Generic;
using System.Linq;

namespace ComposeTestEnvironment.xUnit
{
    /// <summary>
    /// Provide necessary information to discover environment services.
    /// </summary>
    public sealed class Discovery
    {
        private readonly IReadOnlyDictionary<string, (string NewHost, IReadOnlyDictionary<int, int> Ports)> _discoveryInfo;
        private readonly List<Substitution> _substitutions = new();

        public Discovery(IReadOnlyDictionary<HostSubstitution, IReadOnlyList<PortSubstitution>> discoveryInfo)
        {
            _discoveryInfo = discoveryInfo.ToDictionary(
                item => item.Key.OriginalHost,
                item =>
                (
                    NewHost: item.Key.NewHost,
                    Ports: (IReadOnlyDictionary<int, int>)item.Value.ToDictionary(port => port.OriginalPort, port => port.NewPort)
                ));

            string TemplateField(string name, string additional = "")
                => "$(" + name + additional + ")";

            foreach (var (host, ports) in discoveryInfo)
            {
                _substitutions.Add(new Substitution(TemplateField(host.OriginalHost), host.NewHost));
                _substitutions.Add(new Substitution(TemplateField(host.OriginalHost, ".host"), host.NewHost));
                _substitutions.Add(new Substitution(TemplateField(host.OriginalHost, ".port"), ports.First().NewPort.ToString()));

                foreach (var port in ports)
                {
                    _substitutions.Add(new Substitution(
                        TemplateField(host.OriginalHost, "." + port.OriginalPort),
                        port.NewPort.ToString()));

                    _substitutions.Add(new Substitution(
                                           TemplateField(host.OriginalHost, ":" + port.OriginalPort),
                                           host.NewHost + ":" + port.NewPort));
                }
            }
        }

        /// <summary>
        ///  "$(sqlServer.host) $(sqlServer.port) $(sqlServer.1433) $(sqlServer)";
        /// </summary>
        /// <param name="template"></param>
        /// <returns></returns>
        public string Substitute(string template)
        {
            var result = template;
            foreach (var substitution in _substitutions)
            {
                result = result.Replace(substitution.Pattern, substitution.ReplaceWith);
            }

            return result;
        }

        /// <summary>
        /// Get host name for the service (inside docker-compose it will be the service name itself).
        /// </summary>
        /// <param name="serviceName">Name of the service.</param>
        /// <returns>Host name where the specified service can be accessed.</returns>
        public string GetHost(string serviceName)
        {
            return _discoveryInfo[serviceName].NewHost;
        }

        /// <summary>
        /// Get port for the service.
        /// </summary>
        /// <param name="serviceName">Service name.</param>
        /// <param name="originalPort">Original default port.</param>
        /// <returns>Port number (for tests executed under docker compose it will be original port).</returns>
        public int GetPort(string serviceName, int originalPort)
        {
            var ports = _discoveryInfo[serviceName].Ports;
            return ports[originalPort];
        }

        private record Substitution (string Pattern, string ReplaceWith);
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Docker.DotNet;

namespace ComposeTestEnvironment.xUnit
{
    internal sealed class DockerFacade
    {
        public async Task<IReadOnlyList<(ushort PortNumber, string Protocol)>> GetExposedPortsAsync(string image)
        {
            var client = new DockerClientConfiguration().CreateClient();

            var inspectImage = await client.Images.InspectImageAsync(image).ConfigureAwait(false);

            return inspectImage.Config.ExposedPorts.Keys
                .Select(ParsePort)
                .ToList();
        }

        private (ushort PortNumber, string Protocol) ParsePort(string portDefinition)
        {
            var parts = portDefinition.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
            var portNumber = parts.First();
            var protocol = parts.Length == 2 ? parts.Last() : string.Empty;

            return (ushort.Parse(portNumber), protocol);
        }
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Docker.DotNet;

namespace TestCompose
{
    internal class DockerFacade
    {
        public async Task<IReadOnlyList<ushort>> GetExposedPortsAsync(string image)
        {
            var client = new DockerClientConfiguration().CreateClient();

            var inspectImage = await client.Images.InspectImageAsync(image);

            return inspectImage.Config.ExposedPorts.Keys
                .Select(ParsePort)
                .ToList();
        }

        private ushort ParsePort(string portDefinition)
        {
            var portNumber = portDefinition.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries).First();

            return ushort.Parse(portNumber);
        }
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Docker.DotNet;
using Docker.DotNet.Models;

namespace ComposeTestEnvironment.xUnit
{
    internal sealed class DockerFacade
    {
        public async Task<IReadOnlyList<(ushort PortNumber, string Protocol)>> GetExposedPortsAsync(string image)
        {
            var client = new DockerClientConfiguration().CreateClient();

            IDictionary<string,EmptyStruct>? exposedPorts = null;
            try
            {
                await client.Images.CreateImageAsync(
                    new ImagesCreateParameters {FromImage = image,},
                    null,
                    new Progress<JSONMessage>());

                var inspectImage = await client.Images.InspectImageAsync(image).ConfigureAwait(false);


                exposedPorts = inspectImage.Config.ExposedPorts;
            }
            catch (HttpRequestException)
            {
            }

            if (exposedPorts == null)
            {
                return Array.Empty<(ushort PortNumber, string Protocol)>();
            }

            return exposedPorts.Keys
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

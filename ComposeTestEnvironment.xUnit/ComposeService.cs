using System;
using System.Collections.Generic;
using System.Linq;
using YamlDotNet.RepresentationModel;

namespace ComposeTestEnvironment.xUnit
{
    internal sealed class ComposeService
    {
        private readonly YamlMappingNode _serviceDefinition;

        public ComposeService(string serviceName, YamlMappingNode serviceDefinition)
        {
            _serviceDefinition = serviceDefinition;
            serviceDefinition.Children.TryGetValue("image", out var image);

            Image = image?.ToString();
            ServiceName = serviceName;
        }

        public string ServiceName { get; }

        public string? Image { get; }

        public IReadOnlyList<DockerPort> PortMappings { get; private set; } = Array.Empty<DockerPort>();

        public void SetPortMapping(IReadOnlyList<DockerPort> portMapping)
        {
            string Format(DockerPort dockerPort)
            {
                if (string.IsNullOrEmpty(dockerPort.Protocol))
                {
                    return $"{dockerPort.PublicPort}:{dockerPort.ExposedPort}";
                }

                return $"{dockerPort.PublicPort}:{dockerPort.ExposedPort}/{dockerPort.Protocol}";
            }

            _serviceDefinition.Children.Remove("ports");
            _serviceDefinition.Children.Add(
                "ports",
                new YamlSequenceNode(
                    portMapping.Select(dockerPort =>
                                          new YamlScalarNode(Format(dockerPort)))));

            PortMappings = portMapping;
        }
    }
}

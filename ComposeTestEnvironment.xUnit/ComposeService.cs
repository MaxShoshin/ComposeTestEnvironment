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

        public void SetEnvironment(IReadOnlyDictionary<string,string> environment)
        {
            _serviceDefinition.Children.Remove("environment");

            if (!environment.Any())
            {
                return;
            }

            _serviceDefinition.Children.Add(
                "environment",
                new YamlMappingNode(environment.Select(x => new KeyValuePair<YamlNode, YamlNode>(
                                                           new YamlScalarNode(x.Key),
                                                           new YamlScalarNode(x.Value)))));
        }

        public IReadOnlyDictionary<string, string> GetEnvironment()
        {
            if (!_serviceDefinition.Children.TryGetValue("environment", out var environment))
            {
                return new Dictionary<string, string>();
            }

            switch (environment)
            {
                case YamlMappingNode map:
                    // environment:
                    // RACK_ENV: development
                    return map.Children.ToDictionary(x => x.Key.ToString(), x => x.Value.ToString());

                case YamlSequenceNode sequence:
                    // environment:
                    // - RACK_ENV=development
                    return sequence.Children
                        .Select(x => x.ToString().Trim().Split(new[] {'='}, StringSplitOptions.RemoveEmptyEntries))
                        .ToDictionary(x => x[0], x => x[1]);

                default:
                    throw new InvalidOperationException($"Unexpected node type {environment.GetType().Name} for 'environment' node.");
            }
        }
    }
}

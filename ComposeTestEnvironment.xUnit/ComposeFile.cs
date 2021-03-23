using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using YamlDotNet.Core;
using YamlDotNet.RepresentationModel;

namespace ComposeTestEnvironment.xUnit
{
    internal sealed class ComposeFile
    {
        private readonly YamlDocument _doc;

        private ComposeFile(YamlDocument doc, IReadOnlyList<ComposeService> services)
        {
            _doc = doc;
            Services = services;
        }

        public IReadOnlyList<ComposeService> Services { get; private set; }

        public static ComposeFile Parse(Stream stream)
        {
            using var reader = new StreamReader(stream);

            var yamlStream = new YamlStream();
            yamlStream.Load(reader);

            var doc = yamlStream.Documents.Single();

            var rootNode = (YamlMappingNode)doc.RootNode;
            var servicesNode = (YamlMappingNode)rootNode["services"];

            var services = new List<ComposeService>(servicesNode.Children.Count);

            foreach (var serviceNode in servicesNode)
            {
                var serviceName = serviceNode.Key.ToString();
                var serviceDefinition = (YamlMappingNode)serviceNode.Value;

                services.Add(new ComposeService(serviceName, serviceDefinition));
            }

            return new ComposeFile(doc, services);
        }

        public void Save(FileStream tempStream)
        {
            using var textWriter = new StreamWriter(tempStream);

            FixBooleanValues(_doc);

            var yamlStream = new YamlStream(_doc);
            yamlStream.Save(textWriter, assignAnchors: false);
        }

        public void RemoveServices(IReadOnlyList<string> removingServices)
        {
            var rootNode = (YamlMappingNode)_doc.RootNode;
            var servicesNode = (YamlMappingNode)rootNode["services"];

            foreach (var serviceName in removingServices)
            {
                servicesNode.Children.Remove(serviceName);
            }

            var removingServiceNames = removingServices.ToHashSet();

            Services = Services.Where(item => !removingServiceNames.Contains(item.ServiceName)).ToList();
        }


        private void FixBooleanValues(YamlDocument doc)
        {
            foreach (var node in doc.AllNodes.OfType<YamlScalarNode>())
            {
                if (string.Equals(node.Value, "true", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(node.Value, "false", StringComparison.OrdinalIgnoreCase))
                {
                    node.Style = ScalarStyle.DoubleQuoted;
                }
            }
        }
    }
}

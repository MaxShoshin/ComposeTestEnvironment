using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;
using Xunit.Sdk;

// ReSharper disable StaticMemberInGenericType

namespace TestCompose
{
    public class DockerComposeEnvironmentFixture<TDescriptor> : IAsyncLifetime
        where TDescriptor : DockerComposeDescriptor, new()
    {
        private const string ComposeExe = @"docker-compose";

        private static readonly TimeSpan _startTimeout = TimeSpan.FromSeconds(10);

        private static readonly object _syncRoot = new();
        private static Task<Discovery>? _initializeAsync;
        private readonly IMessageSink _output;
        private readonly List<string> _outputStrings = new();
        private readonly DisposableList _disposables = new();
        private readonly TDescriptor _descriptor = new();

        public DockerComposeEnvironmentFixture(IMessageSink output)
        {
            _output = output;

            Discovery = null !; // This class is available only after InitializeAsync. So Discovery will be initialized
        }

        public Discovery Discovery { get; private set; }

        public async Task InitializeAsync()
        {
            if (_initializeAsync == null)
            {
                lock (_syncRoot)
                {
                    _initializeAsync ??= InitializeCoreAsync();
                }
            }

            Discovery = await _initializeAsync;
        }

        public Task DisposeAsync()
        {
            return Task.CompletedTask;
        }

        private async Task<Discovery> InitializeCoreAsync()
        {
            if (_descriptor.IsUnderCompose)
            {
                if (_descriptor.WaitForPortsListen)
                {
                    var listening = _descriptor.Ports
                        .SelectMany(x => x.Value.Select(port => new UriBuilder("tcp://", x.Key, port).Uri))
                        .ToList();

                    await WaitForListeningPorts(listening);
                }

                return new Discovery(
                    _descriptor.Ports.ToDictionary(
                        item => new HostSubstitution(item.Key, item.Key),
                        item => (IReadOnlyList<PortSubstitution>)item.Value.Select(port => new PortSubstitution(port, port)).ToList()));
            }

            return await InitializeComposeEnvironmentAsync();
        }

        private async Task<Discovery> InitializeComposeEnvironmentAsync()
        {
            using var composeFileStream = File.OpenRead(FindFile(_descriptor.FileName));

            var composeFile = ComposeFile.ParseAsync(composeFileStream);

            await AssignExposedPorts(composeFile);

            var generatedFilePath = GenerateComposeFileWithExposedPorts(composeFile);
            var projectName = _descriptor.ProjectName;
            TestFramework.RegisterDisposable(_disposables);

            if (_descriptor.DownOnComplete)
            {
                var downProcess = ComposeDown(generatedFilePath, projectName);
                _disposables.Add(downProcess);

                await _disposables.AddAsync(async () =>
                {
                    WriteMessage("Stopping compose...");
                    await downProcess.Start(_startTimeout);
                    await downProcess.WaitForExit().WithTimeout(_descriptor.StopTimeout);
                });
            }

            var downBeforeCreate = ComposeDown(generatedFilePath, projectName);
            _disposables.Add(downBeforeCreate);
            await downBeforeCreate.Start(_startTimeout);
            await downBeforeCreate.WaitForExit();

            var pullProcess = ComposePull(generatedFilePath, projectName);
            _disposables.Add(pullProcess);
            await pullProcess.Start(TimeSpan.FromMinutes(5));
            await pullProcess.WaitForExit();

            var process = new ProcessHelper(ComposeExe)
                .Argument("-f", generatedFilePath)
                .Argument("-p", projectName)
                .Argument("up")
                .CollectOutput(WriteMessage);

            foreach (var message in _descriptor.StartedMessageMarkers)
            {
                process.WaitForMessageInOutput(message);
            }

            _disposables.Add(process);

            try
            {
                await process.Start(_descriptor.StartTimeout);
            }
            catch (OperationCanceledException ex)
            {
                var processOutput = string.Join(Environment.NewLine, _outputStrings);

                if (_outputStrings.Any(x => x.Contains("No space left on device", StringComparison.Ordinal)))
                {
                    throw new InvalidOperationException("No space left on device. Try to clean volumes via: 'docker volume prune' command.\r\n\r\n", ex);
                }

                throw new OperationCanceledException($"Timeout, docker output:\r\n {processOutput}", ex);
            }

            var portMappings = composeFile.Services
                .Where(service =>
                           service.Image != null &&
                           _descriptor.Ports.ContainsKey(service.ServiceName))
                .ToDictionary(x => x.ServiceName, x => x.PortMappings);

            if (_descriptor.WaitForPortsListen)
            {
                var listening = portMappings.Values
                    .SelectMany(x => x)
                    .Select(x => new UriBuilder("tcp://", "localhost", x.PublicPort).Uri)
                    .ToList();

                await WaitForListeningPorts(listening);
            }

            return new Discovery(
                portMappings.ToDictionary(
                    item => new HostSubstitution(item.Key, "localhost"),
                    item => (IReadOnlyList<PortSubstitution>)item.Value.Select(port => new PortSubstitution(port.ExposedPort, port.PublicPort)).ToList()));
        }

        private async Task WaitForListeningPorts(IReadOnlyList<Uri> listening)
        {
            using var cancellationTokenSource = new CancellationTokenSource(_descriptor.StartTimeout);

            var tasks = listening.Select(uri => Connect(uri, cancellationTokenSource.Token));

            await Task.WhenAll(tasks);
        }

        private async Task Connect(Uri uri, CancellationToken cancellation)
        {
            using var client = new TcpClient();

            try
            {
                while (true)
                {
                    try
                    {
#if NET5_0
                        await client.ConnectAsync(uri.Host, uri.Port, cancellation);
#else
                        await client.ConnectAsync(uri.Host, uri.Port);
#endif

                        return;
                    }
                    catch (SocketException)
                    {
                    }

                    await Task.Delay(TimeSpan.FromMilliseconds(50), cancellation);
                }
            }
            catch (OperationCanceledException ex)
            {
                throw new OperationCanceledException($"Unable to connect to {uri}", ex);
            }
        }

        private ProcessHelper ComposeDown(string composeFile, string projectName)
        {
            var downProcess = new ProcessHelper(ComposeExe)
                .Argument("-f", composeFile)
                .Argument("-p", projectName)
                .Argument("down")
                .Argument("--remove-orphans")
                .Argument("--volumes") // Remove volumes on down to do not eat the space
                .CollectOutput(line => WriteMessage("down: " + line));
            return downProcess;
        }

        private ProcessHelper ComposePull(string composeFile, string projectName)
        {
            var downProcess = new ProcessHelper(ComposeExe)
                .Argument("-f", composeFile)
                .Argument("-p", projectName)
                .Argument("pull")
                .CollectOutput(line => WriteMessage("pull: " + line));
            return downProcess;
        }

        private void WriteMessage(string message)
        {
            _outputStrings.Add(message);

            _output.OnMessage(new DiagnosticMessage(message));
        }

        private string GenerateComposeFileWithExposedPorts(ComposeFile composeFile)
        {
            var generatedComposeFile = GetTempFile();
            _disposables.Add(() =>
            {
                try
                {
                    File.Delete(generatedComposeFile);
                }
                catch (IOException)
                {
                }
            });

            if (_descriptor.GenerateImageBasedCompose)
            {
                var nonImageServices = composeFile.Services
                    .Where(service => service.Image == null)
                    .Select(service => service.ServiceName)
                    .ToList();

                composeFile.RemoveServices(nonImageServices);
            }

            composeFile.RemoveServices(_descriptor.ServicesToRemove);

            using (var tempStream = File.OpenWrite(generatedComposeFile))
            {
                composeFile.Save(tempStream);
            }

            return generatedComposeFile;
        }

        private static async Task AssignExposedPorts(ComposeFile composeFile)
        {
            var docker = new DockerFacade();

            var lastPort = FreePort.DynamicPortStart;

            foreach (var service in composeFile.Services)
            {
                if (service.Image == null)
                {
                    continue;
                }

                var exposedPorts = await docker.GetExposedPortsAsync(service.Image);
                var publicPorts = FreePort.Rent(lastPort, exposedPorts.Count);
                lastPort = (ushort)(publicPorts.Max() + 1);

                var portMapping = exposedPorts
                    .Select((exposedPort, index) => new DockerPort(exposedPort, publicPorts[index]))
                    .ToList();

                service.SetPortMapping(portMapping);
            }
        }

        private string GetTempFile()
        {
            var random = new Random();

            string fileName;
            do
            {
                var prefix = random.Next().ToString("x8");
                fileName = $"~{_descriptor.ProjectName}{prefix}.tmp.yml";
            } while (File.Exists(fileName));

            return fileName;
        }

        private static string FindFile(string name)
        {
            var dir = AppContext.BaseDirectory;
            var fileName = Path.Combine(dir, name);

            while (!File.Exists(fileName))
            {
                var parent = Directory.GetParent(dir);
                if (parent == null)
                {
                    return Path.Combine(AppContext.BaseDirectory, name);
                }

                dir = parent.FullName;
                fileName = Path.Combine(dir, name);
            }

            return fileName;
        }
    }
}

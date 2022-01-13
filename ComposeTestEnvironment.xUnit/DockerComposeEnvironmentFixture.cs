using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using ComposeTestEnvironment.xUnit.Infrastructure;
using Xunit;
using Xunit.Abstractions;
using Xunit.Sdk;

// ReSharper disable StaticMemberInGenericType

namespace ComposeTestEnvironment.xUnit
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

        public DockerComposeEnvironmentFixture(IMessageSink output)
        {
            _output = output;

            Discovery = null !; // This class is available only after InitializeAsync. So Discovery will be initialized
        }

        public Discovery Discovery { get; private set; }

        protected TDescriptor Descriptor { get; } = new();

        protected virtual ValueTask AfterInitializeAsync()
        {
            return default;
        }

        protected virtual ValueTask BeforeDisposeAsync()
        {
            return default;
        }

        protected virtual ValueTask BeforeSingleTimeInitialize()
        {
            return default;
        }

        protected virtual ValueTask AfterSingleTimeInitialize(Discovery discovery, bool usedExistingEnvironment)
        {
            return default;
        }

        protected void RegisterGlobalDisposable(IDisposable disposable)
        {
            _disposables.Add(disposable);
        }

        protected ValueTask RegisterGlobalDisposableAsync(IAsyncDisposable disposable)
        {
            return _disposables.AddAsync(disposable);
        }

        protected virtual Task<bool> IsEnvironmentUp(Discovery discovery)
        {
            return Task.FromResult(true);
        }

        async Task IAsyncLifetime.InitializeAsync()
        {
            if (_initializeAsync == null)
            {
                lock (_syncRoot)
                {
                    _initializeAsync ??= InitializeCoreAsync();
                }
            }

            Discovery = await _initializeAsync.ConfigureAwait(false);

            await AfterInitializeAsync().ConfigureAwait(false);
        }

        async Task IAsyncLifetime.DisposeAsync()
        {
            await BeforeDisposeAsync().ConfigureAwait(false);
        }

        private async Task<Discovery> InitializeCoreAsync()
        {
            await BeforeSingleTimeInitialize().ConfigureAwait(false);

            Discovery discovery;
            bool existingEnv = false;
            if (Descriptor.IsUnderCompose)
            {
                if (Descriptor.WaitForPortsListen)
                {
                    var listening = Descriptor.Ports
                        .SelectMany(x => x.Value.Select(port => new {Service = x.Key, Port = port}))
                        .Where(x => !Descriptor.IgnoreWaitForPortListening.TryGetValue(x.Service, out var prohibitedPorts) ||
                                    !prohibitedPorts.Contains(x.Port))
                        .Select(x => new UriBuilder("tcp://", x.Service, x.Port).Uri)
                        .ToList();

                    await WaitForListeningPorts(listening).ConfigureAwait(false);
                }

                discovery = new Discovery(
                    Descriptor.Ports.ToDictionary(
                        item => new HostSubstitution(item.Key, item.Key),
                        item => (IReadOnlyList<PortSubstitution>)item.Value.Select(port => new PortSubstitution(port, port)).ToList()));
            }
            else if (Descriptor.IsExternalCompose)
            {
                if (Descriptor.WaitForPortsListen)
                {
                    var listening = Descriptor.Ports
                        .SelectMany(x => x.Value.Select(port => new {Service = x.Key, Port = port}))
                        .Where(x => !Descriptor.IgnoreWaitForPortListening.TryGetValue(x.Service, out var prohibitedPorts) ||
                                    !prohibitedPorts.Contains(x.Port))
                        .Select(x => new UriBuilder("tcp://", Descriptor.DockerHost, x.Port).Uri)
                        .ToList();

                    await WaitForListeningPorts(listening).ConfigureAwait(false);
                }

                discovery = new Discovery(
                    Descriptor.Ports.ToDictionary(
                        item => new HostSubstitution(item.Key, Descriptor.DockerHost),
                        item => (IReadOnlyList<PortSubstitution>)item.Value.Select(port => new PortSubstitution(port, port)).ToList()));
            }
            else
            {
                (discovery, existingEnv) = await InitializeComposeEnvironmentAsync().ConfigureAwait(false);
            }

            await Descriptor.WaitForReady(discovery).ConfigureAwait(false);

            await AfterSingleTimeInitialize(discovery, existingEnv).ConfigureAwait(false);

            return discovery;
        }

        private async Task<(Discovery Discovery, bool ExistingEnvironment)> InitializeComposeEnvironmentAsync()
        {
            using var composeFileStream = File.OpenRead(FindFile(Descriptor.ComposeFileName));

            var composeFile = ComposeFile.Parse(composeFileStream);

            await AssignExposedPorts(composeFile, Descriptor.PortRenter).ConfigureAwait(false);

            var (discovery, portMappings) = PortMappings(composeFile);

            if (Descriptor.TryFindExistingEnvironment)
            {
                if (await ArePortsOpened(portMappings).ConfigureAwait(false) &&
                    await IsEnvironmentUp(discovery).ConfigureAwait(false))
                {
                    return (discovery, true);
                }
            }

            await ApplyEnvironmentChanges(composeFile, discovery);

            var generatedFilePath = GenerateComposeFileWithExposedPorts(composeFile);
            var projectName = Descriptor.ProjectName;
            TestFramework.RegisterDisposable(_disposables);

            if (Descriptor.DownOnComplete)
            {
                var downProcess = ComposeDown(generatedFilePath, projectName);
                _disposables.Add(downProcess);

                await _disposables.AddAsync(async () =>
                {
                    WriteMessage("Stopping compose...");
                    await downProcess.Start(_startTimeout).ConfigureAwait(false);
                    await downProcess.WaitForExit().WithTimeout(Descriptor.StopTimeout).ConfigureAwait(false);
                }).ConfigureAwait(false);
            }

            var downBeforeCreate = ComposeDown(generatedFilePath, projectName);
            _disposables.Add(downBeforeCreate);
            await downBeforeCreate.Start(_startTimeout).ConfigureAwait(false);
            await downBeforeCreate.WaitForExit().ConfigureAwait(false);

            var pullProcess = ComposePull(generatedFilePath, projectName);
            _disposables.Add(pullProcess);
            await pullProcess.Start(TimeSpan.FromMinutes(5)).ConfigureAwait(false);
            await pullProcess.WaitForExit().ConfigureAwait(false);

            var process = new ProcessHelper(ComposeExe)
                .Argument("-f", generatedFilePath)
                .Argument("-p", projectName)
                .Argument("up")
                .CollectOutput(WriteMessage);

            if (!Descriptor.GenerateImageBasedCompose)
            {
                process.Argument("--build");
            }

            foreach (var message in Descriptor.StartedMessageMarkers)
            {
                process.WaitForMessageInOutput(message);
            }

            _disposables.Add(process);

            try
            {
                await process.Start(Descriptor.StartTimeout).ConfigureAwait(false);
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

            await WaitForListeningPorts(portMappings);

            return (discovery, false);
        }

        private async Task WaitForListeningPorts(Dictionary<string, IReadOnlyList<DockerPort>> portMappings)
        {
            if (Descriptor.WaitForPortsListen)
            {
                using var cancellationTokenSource = new CancellationTokenSource(Descriptor.StartTimeout);

                var listening = GetListeningUrls(portMappings);

                await Task.WhenAll(listening.Select(x => Connect(x, cancellationTokenSource.Token)));
            }
        }

        private async Task<bool> ArePortsOpened(Dictionary<string, IReadOnlyList<DockerPort>> portMappings)
        {
            if (Descriptor.WaitForPortsListen)
            {
                using var cancellationTokenSource = new CancellationTokenSource(Descriptor.StartTimeout);

                var listening = GetListeningUrls(portMappings);

                var results = await Task.WhenAll(listening.Select(x => CanConnect(x, cancellationTokenSource.Token)))!;

                return results.All(x => x);
            }

            return true;
        }

        private List<Uri> GetListeningUrls(Dictionary<string, IReadOnlyList<DockerPort>> portMappings)
        {
            return portMappings
                .SelectMany(x => x.Value.Select(y => new {Service = x.Key, Port = y}))
                .Where(x => string.IsNullOrEmpty(x.Port.Protocol) || x.Port.Protocol == "tcp")
                .Where(x => !Descriptor.IgnoreWaitForPortListening.TryGetValue(x.Service, out var prohibitedPorts) ||
                            !prohibitedPorts.Contains(x.Port.ExposedPort))
                .Select(x => x.Port)
                .Select(x => new UriBuilder("tcp://", Descriptor.DockerHost, x.PublicPort).Uri)
                .ToList();
        }

        private (Discovery discovery, Dictionary<string, IReadOnlyList<DockerPort>> portMappings) PortMappings(
            ComposeFile composeFile)
        {
            var portMappings = composeFile.Services
                .Where(service =>
                           service.Image != null &&
                           Descriptor.Ports.ContainsKey(service.ServiceName))
                .ToDictionary(x => x.ServiceName, x => x.PortMappings);

            var discovery = new Discovery(
                portMappings.ToDictionary(
                    item => new HostSubstitution(item.Key, Descriptor.DockerHost),
                    item => (IReadOnlyList<PortSubstitution>)item.Value
                        .Select(port => new PortSubstitution(port.ExposedPort, port.PublicPort)).ToList()));

            return (discovery, portMappings);
        }

        private async Task ApplyEnvironmentChanges(ComposeFile composeFile, Discovery discovery)
        {
            foreach (var service in composeFile.Services)
            {
                var existing = service.GetEnvironment();
                var overrides = await Descriptor.GetEnvironment(service.ServiceName, existing, discovery);

                service.SetEnvironment(overrides);
            }
        }

        protected virtual async Task WaitForListeningPorts(IReadOnlyList<Uri> listening)
        {
            using var cancellationTokenSource = new CancellationTokenSource(Descriptor.StartTimeout);

            var tasks = listening.Select(uri => Connect(uri, cancellationTokenSource.Token));

            await Task.WhenAll(tasks).ConfigureAwait(false);
        }

        protected virtual void WriteMessage(string message)
        {
            _outputStrings.Add(message);

            _output.OnMessage(new DiagnosticMessage(message));
        }

        private async Task Connect(Uri uri, CancellationToken cancellation)
        {
            try
            {
                while (!cancellation.IsCancellationRequested)
                {
                    var canConnect = await CanConnect(uri, cancellation).ConfigureAwait(false);

                    if (canConnect)
                    {
                        return;
                    }

                    await Task.Delay(TimeSpan.FromMilliseconds(50), cancellation).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException ex)
            {
                throw new OperationCanceledException($"Unable to connect to {uri}", ex);
            }
        }

        private async Task<bool> CanConnect(Uri uri, CancellationToken cancellation)
        {
            using var client = new TcpClient();

            try
            {
#if NET5_0
                        await client.ConnectAsync(uri.Host, uri.Port, cancellation).ConfigureAwait(false);
#else
                await client.ConnectAsync(uri.Host, uri.Port).ConfigureAwait(false);
#endif

                if (client.Connected)
                {
                    return true;
                }
            }
            catch (SocketException)
            {
            }

            return false;
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

            if (Descriptor.GenerateImageBasedCompose)
            {
                var nonImageServices = composeFile.Services
                    .Where(service => service.Image == null)
                    .Select(service => service.ServiceName)
                    .ToList();

                composeFile.RemoveServices(nonImageServices);
            }

            composeFile.RemoveServices(Descriptor.ServicesToRemove);

            using (var tempStream = File.OpenWrite(generatedComposeFile))
            {
                composeFile.Save(tempStream);
            }

            return generatedComposeFile;
        }

        private async Task AssignExposedPorts(ComposeFile composeFile, IPortRenter portRenter)
        {
            var docker = new DockerFacade();

            foreach (var service in composeFile.Services)
            {
                if (service.Image == null)
                {
                    continue;
                }

                if (!Descriptor.Ports.TryGetValue(service.ServiceName, out var discoveryPorts))
                {
                    continue;
                }

                var imageExposedPorts = await docker.GetExposedPortsAsync(service.Image).ConfigureAwait(false);

                var publicPorts = portRenter.Rent(service.ServiceName, discoveryPorts.Length);

                var portMapping = discoveryPorts
                    .Select((port, index) =>
                    {
                        // Expose only necessary ports, dont expose all ports from image
                        var imagePort = imageExposedPorts.FirstOrDefault(x => x.PortNumber == port);
                        if (imagePort != default)
                        {
                            return new DockerPort(imagePort.PortNumber, publicPorts[index], imagePort.Protocol);
                        }

                        return new DockerPort((ushort)port, publicPorts[index], string.Empty);
                    })
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
                fileName = $"~{Descriptor.ProjectName}{prefix}.tmp.yml";
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

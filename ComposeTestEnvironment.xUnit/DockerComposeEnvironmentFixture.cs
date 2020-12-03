﻿using System;
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

        protected virtual ValueTask AfterSingleTimeInitialize(Discovery discovery)
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

        async Task IAsyncLifetime.InitializeAsync()
        {
            if (_initializeAsync == null)
            {
                lock (_syncRoot)
                {
                    _initializeAsync ??= InitializeCoreAsync();
                }
            }

            Discovery = await _initializeAsync;

            await AfterInitializeAsync();
        }

        async Task IAsyncLifetime.DisposeAsync()
        {
            await BeforeDisposeAsync();
        }

        private async Task<Discovery> InitializeCoreAsync()
        {
            await BeforeSingleTimeInitialize();

            Discovery discovery;
            if (Descriptor.IsUnderCompose)
            {
                if (Descriptor.WaitForPortsListen)
                {
                    var listening = Descriptor.Ports
                        .SelectMany(x => x.Value.Select(port => new UriBuilder("tcp://", x.Key, port).Uri))
                        .ToList();

                    await WaitForListeningPorts(listening);
                }

                discovery = new Discovery(
                    Descriptor.Ports.ToDictionary(
                        item => new HostSubstitution(item.Key, item.Key),
                        item => (IReadOnlyList<PortSubstitution>)item.Value.Select(port => new PortSubstitution(port, port)).ToList()));
            }
            else
            {
                discovery = await InitializeComposeEnvironmentAsync();
            }

            await AfterSingleTimeInitialize(discovery);

            return discovery;
        }

        private async Task<Discovery> InitializeComposeEnvironmentAsync()
        {
            using var composeFileStream = File.OpenRead(FindFile(Descriptor.FileName));

            var composeFile = ComposeFile.ParseAsync(composeFileStream);

            await AssignExposedPorts(composeFile);

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
                    await downProcess.Start(_startTimeout);
                    await downProcess.WaitForExit().WithTimeout(Descriptor.StopTimeout);
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

            foreach (var message in Descriptor.StartedMessageMarkers)
            {
                process.WaitForMessageInOutput(message);
            }

            _disposables.Add(process);

            try
            {
                await process.Start(Descriptor.StartTimeout);
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
                           Descriptor.Ports.ContainsKey(service.ServiceName))
                .ToDictionary(x => x.ServiceName, x => x.PortMappings);

            if (Descriptor.WaitForPortsListen)
            {
                var listening = portMappings.Values
                    .SelectMany(x => x)
                    .Select(x => new UriBuilder("tcp://", "localhost", x.PublicPort).Uri)
                    .ToList();

                await WaitForListeningPorts(listening);
            }

            var discovery = new Discovery(
                portMappings.ToDictionary(
                    item => new HostSubstitution(item.Key, "localhost"),
                    item => (IReadOnlyList<PortSubstitution>)item.Value.Select(port => new PortSubstitution(port.ExposedPort, port.PublicPort)).ToList()));

            await Descriptor.WaitForReady(discovery);

            return discovery;
        }

        private async Task WaitForListeningPorts(IReadOnlyList<Uri> listening)
        {
            using var cancellationTokenSource = new CancellationTokenSource(Descriptor.StartTimeout);

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

                        if (client.Connected)
                        {
                            return;
                        }
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
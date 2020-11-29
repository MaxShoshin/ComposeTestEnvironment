using System;
using System.Collections.Generic;

namespace TestCompose
{
    public abstract class DockerComposeDescriptor
    {
        public virtual bool IsUnderCompose
            => !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("UNDER_COMPOSE"));

        public abstract string FileName { get; }

        public virtual string ProjectName
            => GetType().Assembly.FullName ?? throw new NotSupportedException($"Null is assembly full name is not supported ( typeof({GetType().Name}).Assembly.FullName )");

        public virtual TimeSpan StartTimeout { get; } = TimeSpan.FromSeconds(40);

        public virtual TimeSpan StopTimeout { get; } = TimeSpan.FromSeconds(20);

        public virtual bool DownOnComplete { get; } = true;

        public virtual bool GenerateImageBasedCompose { get; } = true;

        public virtual IReadOnlyList<string> ServicesToRemove { get; } = Array.Empty<string>();

        public virtual IReadOnlyList<string> StartedMessageMarkers { get; } = Array.Empty<string>();

        public virtual bool WaitForPortsListen { get; } = true;

        public abstract IReadOnlyDictionary<string, int[]> Ports { get; }
    }
}

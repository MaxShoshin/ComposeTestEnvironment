using TestCompose;
using Xunit.Abstractions;

namespace Sample
{
    public sealed class SampleComposeFixture : DockerComposeEnvironmentFixture<ComposeDescriptor>
    {
        public SampleComposeFixture(IMessageSink output)
            : base(output)
        {
        }
    }
}

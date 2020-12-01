using System.Threading.Tasks;
using ComposeTestEnvironment.xUnit;
using Xunit.Abstractions;

namespace Sample
{
    public sealed class SampleComposeFixture : DockerComposeEnvironmentFixture<ComposeDescriptor>
    {
        public SampleComposeFixture(IMessageSink output)
            : base(output)
        {
        }

        public string ConnectionString { get; private set; } = string.Empty;

        // You can override various methods to inject your code in initialize/teardown pipeline
        protected override async ValueTask AfterInitializeAsync()
        {
            ConnectionString = Discovery.Substitute(
                "Server=$(sqlserver.host),$(sqlserver.1433);User Id=sa;Password=yourStrong(!)Password;");

            await base.AfterInitializeAsync();
        }
    }
}

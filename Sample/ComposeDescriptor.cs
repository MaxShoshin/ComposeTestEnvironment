using System.Collections.Generic;
using System.Threading.Tasks;
using ComposeTestEnvironment.xUnit;

// It is necessary to run and tear down docker-compose
[assembly: Xunit.TestFramework("ComposeTestEnvironment.xUnit.TestFramework", "ComposeTestEnvironment.xUnit")]

namespace Sample
{
    public class ComposeDescriptor : DockerComposeDescriptor
    {
        // docker-compose file name (it will try to find it in nearest parent directory)
        public override string FileName => "testcompose.yml";

        public override IReadOnlyDictionary<string, int[]> Ports => new Dictionary<string, int[]>
        {
            // Specify all used in tests default service ports
            ["sqlserver"] = new[] {1433},

            ["jaeger"] = new[] { 16686, 5778, 6831 },
        };

        // Additional settings

        // Change to false to do not tear down docker compose after test execution
        public override bool DownOnComplete => true;

        public override IReadOnlyList<string> StartedMessageMarkers => new[]
        {
            // Wait for this message in docker output to detect that SQL Server is started and can accept connection
            // as port listening is not enough to detect correctly started server
            "has been set for engine and full-text services"
        };

        public override Task WaitForReady(Discovery discovery)
        {
            // You can specify additional logic to detect that all services are up and ready
            return Task.CompletedTask;
        }
    }
}

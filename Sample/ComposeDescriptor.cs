using System.Collections.Generic;
using TestCompose;

namespace Sample
{
    public class ComposeDescriptor : DockerComposeDescriptor
    {
        public override string FileName => "testcompose.yml";

        public override IReadOnlyDictionary<string, int[]> Ports => new Dictionary<string, int[]>
        {
            ["sqlserver"] = new[] {1433},
        };

        public override IReadOnlyList<string> StartedMessageMarkers => new[]
        {
            // Wait for this message in docker output to detect that SQL Server is started and can accept connection
            // as port listening is not enough to detect correctly started server
            "Recovery is complete."
        };
    }
}

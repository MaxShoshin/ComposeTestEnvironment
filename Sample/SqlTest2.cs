using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using Xunit;

namespace Sample
{
    public class SqlTest2 : IClassFixture<SampleComposeFixture>
    {
        private readonly SampleComposeFixture _fixture;

        public SqlTest2(SampleComposeFixture fixture)
        {
            _fixture = fixture;
        }

        [Fact]
        public async Task ShouldConnect()
        {
            var connectionString = _fixture.Discovery.Substitute(
                "Server=$(sqlserver.host),$(sqlserver.1433);User Id=sa;Password=yourStrong(!)Password;");

            using var db = new SqlConnection(connectionString);

            await db.OpenAsync();
        }
    }
}

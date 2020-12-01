using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using Xunit;

namespace Sample
{
    public class SqlTest : IClassFixture<SampleComposeFixture>
    {
        private readonly string _connectionString;

        public SqlTest(SampleComposeFixture fixture)
        {
            _connectionString = fixture.ConnectionString;
        }

        [Fact]
        public async Task ShouldConnect()
        {
            using var db = new SqlConnection(_connectionString);

            await db.OpenAsync();
        }
    }
}

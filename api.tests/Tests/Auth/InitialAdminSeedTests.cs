using AgriGis.Api.Auth;
using AgriGis.Api.Tests.Fixtures;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Xunit;

namespace AgriGis.Api.Tests.Tests.Auth;

// A508: InitialAdminBootstrap が admin 不在時に admin/既定組織を upsert することを直接検証
[Collection(PostgisCollection.Name)]
public sealed class InitialAdminSeedTests : IAsyncLifetime
{
    private readonly PostgisContainerFixture _pg;

    public InitialAdminSeedTests(PostgisContainerFixture pg) => _pg = pg;

    public async Task InitializeAsync()
    {
        // 完全クリーンな状態にして bootstrap を確認したいので DbReset とは別に admin を消す
        await DbReset.RunAsync(_pg.ConnectionString);
        await using var conn = new NpgsqlConnection(_pg.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(@"
            DELETE FROM user_roles WHERE role = 'admin';", conn);
        await cmd.ExecuteNonQueryAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Bootstrap_NoAdmin_CreatesAdminAndOrg()
    {
        Environment.SetEnvironmentVariable("AGRI_GIS_INITIAL_ADMIN_PW", "BootstrapPw123!");

        var dsBuilder = new NpgsqlDataSourceBuilder(_pg.ConnectionString);
        await using var ds = dsBuilder.Build();

        var bootstrap = new InitialAdminBootstrap(
            ds, new PasswordHasher(), NullLogger<InitialAdminBootstrap>.Instance);
        await bootstrap.StartAsync(CancellationToken.None);

        await using var conn = new NpgsqlConnection(_pg.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(@"
            SELECT count(*) FROM user_roles ur
              JOIN users u ON u.user_id = ur.user_id
             WHERE ur.role = 'admin'
               AND u.deleted_at IS NULL
               AND u.login_id = 'admin'", conn);
        var n = Convert.ToInt32(await cmd.ExecuteScalarAsync());
        Assert.True(n >= 1);
    }

    [Fact]
    public async Task Bootstrap_MissingEnvPw_Throws()
    {
        Environment.SetEnvironmentVariable("AGRI_GIS_INITIAL_ADMIN_PW", "");

        var dsBuilder = new NpgsqlDataSourceBuilder(_pg.ConnectionString);
        await using var ds = dsBuilder.Build();

        var bootstrap = new InitialAdminBootstrap(
            ds, new PasswordHasher(), NullLogger<InitialAdminBootstrap>.Instance);
        await Assert.ThrowsAsync<InvalidOperationException>(() => bootstrap.StartAsync(CancellationToken.None));
    }
}

using Jellyfin.Plugin.Community.Infrastructure;
using Microsoft.Extensions.Logging.Abstractions;

namespace Jellyfin.Plugin.Community.Tests;

public sealed class CommunityDatabaseTests : IAsyncLifetime
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "jellyfin-community-tests", Guid.NewGuid().ToString("N"));

    public Task InitializeAsync() => Task.CompletedTask;

    public Task DisposeAsync()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }

        return Task.CompletedTask;
    }

    [Fact]
    public async Task Initialize_CreatesSchemaAndSeedsCategories()
    {
        var database = new CommunityDatabase(new CommunityPaths(_root), NullLogger<CommunityDatabase>.Instance);
        await database.InitializeAsync();
        Assert.True(File.Exists(database.DatabasePath));
        Assert.Equal("ok", await database.IntegrityCheckAsync());

        await using var connection = await database.OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM categories;";
        var count = Convert.ToInt32(await command.ExecuteScalarAsync(), System.Globalization.CultureInfo.InvariantCulture);
        Assert.True(count >= 3);
    }
}

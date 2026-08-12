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

    [Fact]
    public async Task Initialize_Existing140DatabasePreservesForumData()
    {
        var paths = new CommunityPaths(_root);
        var version140 = new CommunityDatabase(paths, NullLogger<CommunityDatabase>.Instance);
        await version140.InitializeAsync();

        await using (var connection = await version140.OpenConnectionAsync())
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                INSERT INTO threads(
                    category_id, kind, title, author_user_id, author_name,
                    requires_approval, approved_utc, approved_by_user_id,
                    created_utc, updated_utc, last_activity_utc)
                VALUES(
                    (SELECT id FROM categories WHERE slug = 'general'), 0, 'Tema conservado de 1.4',
                    '11111111-1111-1111-1111-111111111111', 'usuario-14',
                    0, $now, '11111111-1111-1111-1111-111111111111', $now, $now, $now);
                INSERT INTO posts(
                    thread_id, author_user_id, author_name, body_markdown, body_html,
                    created_utc, updated_utc)
                VALUES(
                    last_insert_rowid(), '11111111-1111-1111-1111-111111111111', 'usuario-14',
                    'Contenido anterior', '<p>Contenido anterior</p>', $now, $now);
                """;
            command.Parameters.AddWithValue("$now", DateTime.UtcNow.ToString("O", System.Globalization.CultureInfo.InvariantCulture));
            await command.ExecuteNonQueryAsync();
        }

        var version150 = new CommunityDatabase(paths, NullLogger<CommunityDatabase>.Instance);
        await version150.InitializeAsync();
        Assert.Equal("ok", await version150.IntegrityCheckAsync());

        await using var upgradedConnection = await version150.OpenConnectionAsync();
        await using var upgradedCommand = upgradedConnection.CreateCommand();
        upgradedCommand.CommandText = """
            SELECT COUNT(*)
            FROM threads t
            JOIN posts p ON p.thread_id = t.id
            WHERE t.title = 'Tema conservado de 1.4' AND p.body_markdown = 'Contenido anterior';
            """;
        Assert.Equal(1L, Convert.ToInt64(await upgradedCommand.ExecuteScalarAsync(), System.Globalization.CultureInfo.InvariantCulture));
    }
}

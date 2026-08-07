using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Community.Infrastructure;

public sealed class CommunityDatabase
{
    private const int SchemaVersion = 1;
    private readonly CommunityPaths _paths;
    private readonly ILogger<CommunityDatabase> _logger;
    private readonly SemaphoreSlim _initializationLock = new(1, 1);
    private volatile bool _initialized;

    public CommunityDatabase(CommunityPaths paths, ILogger<CommunityDatabase> logger)
    {
        _paths = paths;
        _logger = logger;
    }

    public string DatabasePath => _paths.DatabasePath;

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        if (_initialized)
        {
            return;
        }

        await _initializationLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_initialized)
            {
                return;
            }

            _paths.EnsureCreated();
            await using var connection = await OpenUninitializedConnectionAsync(cancellationToken).ConfigureAwait(false);
            await ExecutePragmasAsync(connection, cancellationToken).ConfigureAwait(false);
            await CreateSchemaAsync(connection, cancellationToken).ConfigureAwait(false);
            await SeedAsync(connection, cancellationToken).ConfigureAwait(false);
            _initialized = true;
        }
        finally
        {
            _initializationLock.Release();
        }
    }

    public async Task<SqliteConnection> OpenConnectionAsync(CancellationToken cancellationToken = default)
    {
        await InitializeAsync(cancellationToken).ConfigureAwait(false);
        var connection = await OpenUninitializedConnectionAsync(cancellationToken).ConfigureAwait(false);
        await ExecutePragmasAsync(connection, cancellationToken).ConfigureAwait(false);
        return connection;
    }

    public async Task CheckpointAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA wal_checkpoint(TRUNCATE);";
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task OptimizeAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA optimize; ANALYZE;";
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<string> IntegrityCheckAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA integrity_check;";
        return Convert.ToString(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false), System.Globalization.CultureInfo.InvariantCulture) ?? "unknown";
    }

    public long GetDatabaseSizeBytes()
    {
        long total = 0;
        foreach (var suffix in new[] { string.Empty, "-wal", "-shm" })
        {
            var path = _paths.DatabasePath + suffix;
            if (File.Exists(path))
            {
                total += new FileInfo(path).Length;
            }
        }

        return total;
    }

    public void ResetInitializationState() => _initialized = false;

    private async Task<SqliteConnection> OpenUninitializedConnectionAsync(CancellationToken cancellationToken)
    {
        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = _paths.DatabasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared,
            Pooling = true,
            DefaultTimeout = 10
        };
        var connection = new SqliteConnection(builder.ToString());
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        return connection;
    }

    private static async Task ExecutePragmasAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            PRAGMA foreign_keys = ON;
            PRAGMA journal_mode = WAL;
            PRAGMA synchronous = NORMAL;
            PRAGMA busy_timeout = 10000;
            PRAGMA temp_store = MEMORY;
            """;
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task CreateSchemaAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.Transaction = (SqliteTransaction)transaction;
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS schema_info (
                version INTEGER NOT NULL,
                applied_utc TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS known_users (
                user_id TEXT PRIMARY KEY,
                username TEXT NOT NULL COLLATE NOCASE,
                first_seen_utc TEXT NOT NULL,
                last_seen_utc TEXT NOT NULL,
                is_deleted INTEGER NOT NULL DEFAULT 0
            );
            CREATE UNIQUE INDEX IF NOT EXISTS ux_known_users_username ON known_users(username COLLATE NOCASE) WHERE is_deleted = 0;

            CREATE TABLE IF NOT EXISTS categories (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                name TEXT NOT NULL,
                slug TEXT NOT NULL UNIQUE COLLATE NOCASE,
                description TEXT NULL,
                library_id TEXT NULL,
                sort_order INTEGER NOT NULL DEFAULT 0,
                is_read_only INTEGER NOT NULL DEFAULT 0,
                requires_approval INTEGER NOT NULL DEFAULT 0,
                is_archived INTEGER NOT NULL DEFAULT 0,
                created_utc TEXT NOT NULL,
                updated_utc TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS forum_roles (
                user_id TEXT PRIMARY KEY,
                role TEXT NOT NULL CHECK(role IN ('moderator')),
                category_id INTEGER NULL REFERENCES categories(id) ON DELETE CASCADE,
                created_utc TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS user_forum_status (
                user_id TEXT PRIMARY KEY,
                is_suspended INTEGER NOT NULL DEFAULT 0,
                suspended_until_utc TEXT NULL,
                is_muted INTEGER NOT NULL DEFAULT 0,
                muted_until_utc TEXT NULL,
                reason TEXT NULL,
                updated_by_user_id TEXT NULL,
                updated_utc TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS threads (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                category_id INTEGER NOT NULL REFERENCES categories(id),
                kind INTEGER NOT NULL DEFAULT 0,
                title TEXT NOT NULL,
                author_user_id TEXT NOT NULL,
                author_name TEXT NOT NULL,
                item_id TEXT NULL,
                item_name TEXT NULL,
                is_pinned INTEGER NOT NULL DEFAULT 0,
                is_locked INTEGER NOT NULL DEFAULT 0,
                is_archived INTEGER NOT NULL DEFAULT 0,
                is_hidden INTEGER NOT NULL DEFAULT 0,
                requires_approval INTEGER NOT NULL DEFAULT 0,
                approved_utc TEXT NULL,
                approved_by_user_id TEXT NULL,
                view_count INTEGER NOT NULL DEFAULT 0,
                reply_count INTEGER NOT NULL DEFAULT 0,
                created_utc TEXT NOT NULL,
                updated_utc TEXT NOT NULL,
                last_activity_utc TEXT NOT NULL,
                deleted_utc TEXT NULL
            );
            CREATE INDEX IF NOT EXISTS ix_threads_category_activity ON threads(category_id, is_pinned DESC, last_activity_utc DESC);
            CREATE INDEX IF NOT EXISTS ix_threads_item ON threads(item_id, last_activity_utc DESC);
            CREATE INDEX IF NOT EXISTS ix_threads_author ON threads(author_user_id, created_utc DESC);

            CREATE TABLE IF NOT EXISTS posts (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                thread_id INTEGER NOT NULL REFERENCES threads(id) ON DELETE CASCADE,
                parent_post_id INTEGER NULL REFERENCES posts(id),
                author_user_id TEXT NOT NULL,
                author_name TEXT NOT NULL,
                body_markdown TEXT NOT NULL,
                body_html TEXT NOT NULL,
                contains_spoiler INTEGER NOT NULL DEFAULT 0,
                spoiler_item_id TEXT NULL,
                spoiler_label TEXT NULL,
                is_edited INTEGER NOT NULL DEFAULT 0,
                is_hidden INTEGER NOT NULL DEFAULT 0,
                is_deleted INTEGER NOT NULL DEFAULT 0,
                created_utc TEXT NOT NULL,
                updated_utc TEXT NOT NULL,
                deleted_utc TEXT NULL
            );
            CREATE INDEX IF NOT EXISTS ix_posts_thread_created ON posts(thread_id, created_utc);
            CREATE INDEX IF NOT EXISTS ix_posts_author_created ON posts(author_user_id, created_utc DESC);

            CREATE TABLE IF NOT EXISTS post_edits (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                post_id INTEGER NOT NULL REFERENCES posts(id) ON DELETE CASCADE,
                editor_user_id TEXT NOT NULL,
                old_body_markdown TEXT NOT NULL,
                new_body_markdown TEXT NOT NULL,
                reason TEXT NULL,
                created_utc TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS tags (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                name TEXT NOT NULL UNIQUE COLLATE NOCASE,
                slug TEXT NOT NULL UNIQUE COLLATE NOCASE
            );
            CREATE TABLE IF NOT EXISTS thread_tags (
                thread_id INTEGER NOT NULL REFERENCES threads(id) ON DELETE CASCADE,
                tag_id INTEGER NOT NULL REFERENCES tags(id) ON DELETE CASCADE,
                PRIMARY KEY(thread_id, tag_id)
            );

            CREATE TABLE IF NOT EXISTS reactions (
                post_id INTEGER NOT NULL REFERENCES posts(id) ON DELETE CASCADE,
                user_id TEXT NOT NULL,
                reaction TEXT NOT NULL,
                created_utc TEXT NOT NULL,
                PRIMARY KEY(post_id, user_id)
            );

            CREATE TABLE IF NOT EXISTS thread_follows (
                thread_id INTEGER NOT NULL REFERENCES threads(id) ON DELETE CASCADE,
                user_id TEXT NOT NULL,
                created_utc TEXT NOT NULL,
                PRIMARY KEY(thread_id, user_id)
            );

            CREATE TABLE IF NOT EXISTS read_state (
                thread_id INTEGER NOT NULL REFERENCES threads(id) ON DELETE CASCADE,
                user_id TEXT NOT NULL,
                last_read_post_id INTEGER NULL,
                read_utc TEXT NOT NULL,
                PRIMARY KEY(thread_id, user_id)
            );

            CREATE TABLE IF NOT EXISTS notifications (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                user_id TEXT NOT NULL,
                type INTEGER NOT NULL,
                title TEXT NOT NULL,
                message TEXT NOT NULL,
                thread_id INTEGER NULL REFERENCES threads(id) ON DELETE CASCADE,
                post_id INTEGER NULL REFERENCES posts(id) ON DELETE CASCADE,
                is_read INTEGER NOT NULL DEFAULT 0,
                created_utc TEXT NOT NULL,
                expires_utc TEXT NOT NULL
            );
            CREATE INDEX IF NOT EXISTS ix_notifications_user_created ON notifications(user_id, is_read, created_utc DESC);

            CREATE TABLE IF NOT EXISTS reports (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                post_id INTEGER NOT NULL REFERENCES posts(id) ON DELETE CASCADE,
                thread_id INTEGER NOT NULL REFERENCES threads(id) ON DELETE CASCADE,
                reporter_user_id TEXT NOT NULL,
                reporter_name TEXT NOT NULL,
                reason TEXT NOT NULL,
                comment TEXT NULL,
                state INTEGER NOT NULL DEFAULT 0,
                assigned_moderator_user_id TEXT NULL,
                resolution TEXT NULL,
                created_utc TEXT NOT NULL,
                updated_utc TEXT NOT NULL,
                UNIQUE(post_id, reporter_user_id)
            );
            CREATE INDEX IF NOT EXISTS ix_reports_state_created ON reports(state, created_utc);

            CREATE TABLE IF NOT EXISTS moderation_actions (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                actor_user_id TEXT NOT NULL,
                actor_name TEXT NOT NULL,
                action TEXT NOT NULL,
                entity_type TEXT NOT NULL,
                entity_id TEXT NOT NULL,
                reason TEXT NULL,
                before_json TEXT NULL,
                after_json TEXT NULL,
                created_utc TEXT NOT NULL
            );
            CREATE INDEX IF NOT EXISTS ix_moderation_actions_created ON moderation_actions(created_utc DESC);

            CREATE TABLE IF NOT EXISTS polls (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                thread_id INTEGER NOT NULL UNIQUE REFERENCES threads(id) ON DELETE CASCADE,
                question TEXT NOT NULL,
                allow_multiple INTEGER NOT NULL DEFAULT 0,
                closes_utc TEXT NULL,
                created_utc TEXT NOT NULL
            );
            CREATE TABLE IF NOT EXISTS poll_options (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                poll_id INTEGER NOT NULL REFERENCES polls(id) ON DELETE CASCADE,
                option_text TEXT NOT NULL,
                sort_order INTEGER NOT NULL
            );
            CREATE TABLE IF NOT EXISTS poll_votes (
                poll_id INTEGER NOT NULL REFERENCES polls(id) ON DELETE CASCADE,
                option_id INTEGER NOT NULL REFERENCES poll_options(id) ON DELETE CASCADE,
                user_id TEXT NOT NULL,
                created_utc TEXT NOT NULL,
                PRIMARY KEY(option_id, user_id)
            );
            CREATE INDEX IF NOT EXISTS ix_poll_votes_poll_user ON poll_votes(poll_id, user_id);

            CREATE TABLE IF NOT EXISTS attachments (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                post_id INTEGER NOT NULL REFERENCES posts(id) ON DELETE CASCADE,
                uploader_user_id TEXT NOT NULL,
                original_name TEXT NOT NULL,
                stored_name TEXT NOT NULL UNIQUE,
                media_type TEXT NOT NULL,
                size_bytes INTEGER NOT NULL,
                sha256 TEXT NOT NULL,
                created_utc TEXT NOT NULL,
                deleted_utc TEXT NULL
            );
            CREATE INDEX IF NOT EXISTS ix_attachments_post ON attachments(post_id);

            CREATE TABLE IF NOT EXISTS drafts (
                user_id TEXT NOT NULL,
                draft_key TEXT NOT NULL,
                body TEXT NOT NULL,
                metadata_json TEXT NULL,
                updated_utc TEXT NOT NULL,
                PRIMARY KEY(user_id, draft_key)
            );

            CREATE TABLE IF NOT EXISTS settings (
                setting_key TEXT PRIMARY KEY,
                setting_value TEXT NOT NULL,
                updated_utc TEXT NOT NULL
            );
            """;
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

        await using var versionCommand = connection.CreateCommand();
        versionCommand.Transaction = (SqliteTransaction)transaction;
        versionCommand.CommandText = "SELECT COALESCE(MAX(version), 0) FROM schema_info;";
        var currentVersion = Convert.ToInt32(await versionCommand.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false), System.Globalization.CultureInfo.InvariantCulture);
        if (currentVersion > SchemaVersion)
        {
            throw new InvalidOperationException($"Community database schema {currentVersion} is newer than supported schema {SchemaVersion}.");
        }

        if (currentVersion < SchemaVersion)
        {
            await using var insertVersion = connection.CreateCommand();
            insertVersion.Transaction = (SqliteTransaction)transaction;
            insertVersion.CommandText = "INSERT INTO schema_info(version, applied_utc) VALUES ($version, $utc);";
            insertVersion.Parameters.AddWithValue("$version", SchemaVersion);
            insertVersion.Parameters.AddWithValue("$utc", DateTime.UtcNow.ToString("O", System.Globalization.CultureInfo.InvariantCulture));
            await insertVersion.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            await using var fts = connection.CreateCommand();
            fts.CommandText = """
                CREATE VIRTUAL TABLE IF NOT EXISTS community_fts USING fts5(
                    entity_type UNINDEXED,
                    entity_id UNINDEXED,
                    title,
                    body,
                    author,
                    tokenize='unicode61 remove_diacritics 2'
                );
                """;
            await fts.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (SqliteException ex)
        {
            _logger.LogWarning(ex, "SQLite FTS5 is unavailable. Community search will use indexed LIKE queries.");
        }
    }

    private static async Task SeedAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow.ToString("O", System.Globalization.CultureInfo.InvariantCulture);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT OR IGNORE INTO categories(name, slug, description, sort_order, created_utc, updated_utc)
            VALUES
                ('General', 'general', 'Conversaciones generales del servidor.', 0, $now, $now),
                ('Recomendaciones', 'recomendaciones', 'Recomendaciones de películas, series y música.', 10, $now, $now),
                ('Anuncios', 'anuncios', 'Anuncios y novedades del servidor.', 20, $now, $now);
            """;
        command.Parameters.AddWithValue("$now", now);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }
}

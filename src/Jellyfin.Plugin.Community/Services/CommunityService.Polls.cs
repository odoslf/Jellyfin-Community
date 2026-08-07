using Jellyfin.Plugin.Community.Domain;
using Microsoft.Data.Sqlite;

namespace Jellyfin.Plugin.Community.Services;

public sealed partial class CommunityService
{
    public async Task<PollDto> VoteAsync(CommunityUserContext user, long threadId, VotePollRequest request, CancellationToken cancellationToken)
    {
        _permissions.EnsureCanWrite(user);
        await using var connection = await _database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await GetThreadSummaryAsync(connection, user, threadId, cancellationToken).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        await using var pollCommand = connection.CreateCommand();
        pollCommand.Transaction = (SqliteTransaction)transaction;
        pollCommand.CommandText = "SELECT id, allow_multiple, closes_utc FROM polls WHERE thread_id = $threadId;";
        pollCommand.Parameters.AddWithValue("$threadId", threadId);
        await using var reader = await pollCommand.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            throw new CommunityNotFoundException("Poll not found.");
        }

        var pollId = reader.GetInt64(0);
        var allowMultiple = ReadBool(reader, 1);
        var closes = ParseNullableDate(reader, 2);
        if (closes is not null && closes <= DateTime.UtcNow)
        {
            throw new CommunityValidationException("The poll is closed.");
        }

        var optionIds = request.OptionIds.Distinct().ToArray();
        if (optionIds.Length == 0 || (!allowMultiple && optionIds.Length != 1))
        {
            throw new CommunityValidationException(allowMultiple ? "Select at least one option." : "Select exactly one option.");
        }

        await reader.DisposeAsync().ConfigureAwait(false);
        await using (var validate = connection.CreateCommand())
        {
            validate.Transaction = (SqliteTransaction)transaction;
            var names = new List<string>();
            for (var i = 0; i < optionIds.Length; i++)
            {
                var name = "$option" + i.ToString(System.Globalization.CultureInfo.InvariantCulture);
                names.Add(name);
                validate.Parameters.AddWithValue(name, optionIds[i]);
            }

            validate.CommandText = $"SELECT COUNT(*) FROM poll_options WHERE poll_id = $pollId AND id IN ({string.Join(',', names)});";
            validate.Parameters.AddWithValue("$pollId", pollId);
            var valid = Convert.ToInt32(await validate.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false), System.Globalization.CultureInfo.InvariantCulture);
            if (valid != optionIds.Length)
            {
                throw new CommunityValidationException("One or more poll options are invalid.");
            }
        }

        await using (var clear = connection.CreateCommand())
        {
            clear.Transaction = (SqliteTransaction)transaction;
            clear.CommandText = "DELETE FROM poll_votes WHERE poll_id = $pollId AND user_id = $userId;";
            clear.Parameters.AddWithValue("$pollId", pollId);
            clear.Parameters.AddWithValue("$userId", user.UserId.ToString("D"));
            await clear.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        foreach (var optionId in optionIds)
        {
            await using var insert = connection.CreateCommand();
            insert.Transaction = (SqliteTransaction)transaction;
            insert.CommandText = "INSERT INTO poll_votes(poll_id, option_id, user_id, created_utc) VALUES($pollId, $optionId, $userId, $now);";
            insert.Parameters.AddWithValue("$pollId", pollId);
            insert.Parameters.AddWithValue("$optionId", optionId);
            insert.Parameters.AddWithValue("$userId", user.UserId.ToString("D"));
            insert.Parameters.AddWithValue("$now", Format(DateTime.UtcNow));
            await insert.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return (await GetPollAsync(connection, user.UserId, threadId, cancellationToken).ConfigureAwait(false))!;
    }

    private static async Task CreatePollInternalAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        long threadId,
        CreatePollRequest request,
        DateTime now,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Question) || request.Question.Trim().Length > 300)
        {
            throw new CommunityValidationException("Poll question must contain between 1 and 300 characters.");
        }

        var options = request.Options.Where(option => !string.IsNullOrWhiteSpace(option)).Select(option => option.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (options.Length < 2 || options.Length > 20 || options.Any(option => option.Length > 200))
        {
            throw new CommunityValidationException("A poll requires between 2 and 20 unique options, each up to 200 characters.");
        }

        if (request.ClosesUtc is not null && request.ClosesUtc <= now)
        {
            throw new CommunityValidationException("Poll closing time must be in the future.");
        }

        await using var insertPoll = connection.CreateCommand();
        insertPoll.Transaction = transaction;
        insertPoll.CommandText = """
            INSERT INTO polls(thread_id, question, allow_multiple, closes_utc, created_utc)
            VALUES($threadId, $question, $multiple, $closes, $now);
            SELECT last_insert_rowid();
            """;
        insertPoll.Parameters.AddWithValue("$threadId", threadId);
        insertPoll.Parameters.AddWithValue("$question", request.Question.Trim());
        insertPoll.Parameters.AddWithValue("$multiple", request.AllowMultiple ? 1 : 0);
        insertPoll.Parameters.AddWithValue("$closes", request.ClosesUtc is null ? DBNull.Value : Format(request.ClosesUtc.Value));
        insertPoll.Parameters.AddWithValue("$now", Format(now));
        var pollId = Convert.ToInt64(await insertPoll.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false), System.Globalization.CultureInfo.InvariantCulture);
        for (var i = 0; i < options.Length; i++)
        {
            await using var option = connection.CreateCommand();
            option.Transaction = transaction;
            option.CommandText = "INSERT INTO poll_options(poll_id, option_text, sort_order) VALUES($pollId, $text, $sort);";
            option.Parameters.AddWithValue("$pollId", pollId);
            option.Parameters.AddWithValue("$text", options[i]);
            option.Parameters.AddWithValue("$sort", i);
            await option.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    private static async Task<PollDto?> GetPollAsync(SqliteConnection connection, Guid userId, long threadId, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT id, question, allow_multiple, closes_utc FROM polls WHERE thread_id = $threadId;";
        command.Parameters.AddWithValue("$threadId", threadId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        var pollId = reader.GetInt64(0);
        var question = reader.GetString(1);
        var allowMultiple = ReadBool(reader, 2);
        var closes = ParseNullableDate(reader, 3);
        await reader.DisposeAsync().ConfigureAwait(false);
        var options = new List<PollOptionDto>();
        await using var optionCommand = connection.CreateCommand();
        optionCommand.CommandText = """
            SELECT o.id, o.option_text, COUNT(v.option_id),
                   EXISTS(SELECT 1 FROM poll_votes mine WHERE mine.option_id = o.id AND mine.user_id = $userId)
            FROM poll_options o
            LEFT JOIN poll_votes v ON v.option_id = o.id
            WHERE o.poll_id = $pollId
            GROUP BY o.id
            ORDER BY o.sort_order;
            """;
        optionCommand.Parameters.AddWithValue("$pollId", pollId);
        optionCommand.Parameters.AddWithValue("$userId", userId.ToString("D"));
        await using var optionReader = await optionCommand.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await optionReader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            options.Add(new PollOptionDto(optionReader.GetInt64(0), optionReader.GetString(1), optionReader.GetInt32(2), ReadBool(optionReader, 3)));
        }

        return new PollDto(pollId, threadId, question, allowMultiple, closes, options);
    }
}

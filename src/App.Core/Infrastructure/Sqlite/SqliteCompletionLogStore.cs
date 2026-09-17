using App.Core.Domain.Repositories;

namespace App.Core.Infrastructure.Sqlite;

public sealed class SqliteCompletionLogStore : ICompletionLogStore
{
    private readonly string _connectionString;

    public SqliteCompletionLogStore(string connectionString)
    {
        _connectionString = connectionString;
    }

    public void Append(Guid todoItemId, DateTime completedOn, string? checklistSnapshot)
    {
        using var connection = SqliteConnectionHelper.OpenConnection(_connectionString);
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO CompletionLog (Id, TodoItemId, CompletedOn, ChecklistSnapshot)
            VALUES ($Id, $TodoItemId, $CompletedOn, $ChecklistSnapshot);
            """;
        command.Parameters.AddWithValue("$Id", Guid.NewGuid().ToString());
        command.Parameters.AddWithValue("$TodoItemId", todoItemId.ToString());
        command.Parameters.AddWithValue("$CompletedOn", completedOn);
        command.Parameters.AddWithValue("$ChecklistSnapshot", (object?)checklistSnapshot ?? DBNull.Value);
        command.ExecuteNonQuery();
    }
}

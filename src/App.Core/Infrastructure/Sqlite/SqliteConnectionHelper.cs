using Microsoft.Data.Sqlite;

namespace App.Core.Infrastructure.Sqlite;

internal static class SqliteConnectionHelper
{
    /// <summary>커넥션을 열고 외래 키 제약(ON DELETE CASCADE 등)을 활성화해서 반환한다</summary>
    public static SqliteConnection OpenConnection(string connectionString)
    {
        var connection = new SqliteConnection(connectionString);
        connection.Open();

        using var pragmaCommand = connection.CreateCommand();
        pragmaCommand.CommandText = "PRAGMA foreign_keys = ON;";
        pragmaCommand.ExecuteNonQuery();

        return connection;
    }
}

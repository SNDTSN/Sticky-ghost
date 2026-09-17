using App.Core.Domain.Entities;
using App.Core.Domain.Repositories;
using Microsoft.Data.Sqlite;

namespace App.Core.Infrastructure.Sqlite;

public sealed class SqliteMemoRepository : IMemoRepository
{
    private readonly string _connectionString;

    public SqliteMemoRepository(string connectionString)
    {
        _connectionString = connectionString;
    }

    public void Save(MemoNote memo)
    {
        using var connection = SqliteConnectionHelper.OpenConnection(_connectionString);
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO MemoNote
                (Id, Content, Color, PositionX, PositionY, Width, Height, IsPinned, CreatedAt, UpdatedAt)
            VALUES
                ($Id, $Content, $Color, $PositionX, $PositionY, $Width, $Height, $IsPinned, $CreatedAt, $UpdatedAt)
            ON CONFLICT(Id) DO UPDATE SET
                Content = excluded.Content,
                Color = excluded.Color,
                PositionX = excluded.PositionX,
                PositionY = excluded.PositionY,
                Width = excluded.Width,
                Height = excluded.Height,
                IsPinned = excluded.IsPinned,
                UpdatedAt = excluded.UpdatedAt;
            """;

        command.Parameters.AddWithValue("$Id", memo.Id.ToString());
        command.Parameters.AddWithValue("$Content", memo.Content);
        command.Parameters.AddWithValue("$Color", memo.Color);
        command.Parameters.AddWithValue("$PositionX", memo.PositionX);
        command.Parameters.AddWithValue("$PositionY", memo.PositionY);
        command.Parameters.AddWithValue("$Width", memo.Width);
        command.Parameters.AddWithValue("$Height", memo.Height);
        command.Parameters.AddWithValue("$IsPinned", memo.IsPinned);
        command.Parameters.AddWithValue("$CreatedAt", memo.CreatedAt);
        command.Parameters.AddWithValue("$UpdatedAt", memo.UpdatedAt);

        command.ExecuteNonQuery();
    }

    public void Delete(Guid id)
    {
        using var connection = SqliteConnectionHelper.OpenConnection(_connectionString);
        using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM MemoNote WHERE Id = $Id;";
        command.Parameters.AddWithValue("$Id", id.ToString());
        command.ExecuteNonQuery();
    }

    public IEnumerable<MemoNote> GetAll()
    {
        using var connection = SqliteConnectionHelper.OpenConnection(_connectionString);
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT * FROM MemoNote;";

        using var reader = command.ExecuteReader();
        var memos = new List<MemoNote>();
        while (reader.Read())
        {
            memos.Add(new MemoNote
            {
                Id = Guid.Parse(reader.GetString(reader.GetOrdinal("Id"))),
                Content = reader.GetString(reader.GetOrdinal("Content")),
                Color = reader.GetString(reader.GetOrdinal("Color")),
                PositionX = reader.GetDouble(reader.GetOrdinal("PositionX")),
                PositionY = reader.GetDouble(reader.GetOrdinal("PositionY")),
                Width = reader.GetDouble(reader.GetOrdinal("Width")),
                Height = reader.GetDouble(reader.GetOrdinal("Height")),
                IsPinned = reader.GetBoolean(reader.GetOrdinal("IsPinned")),
                CreatedAt = reader.GetDateTime(reader.GetOrdinal("CreatedAt")),
                UpdatedAt = reader.GetDateTime(reader.GetOrdinal("UpdatedAt")),
            });
        }
        return memos;
    }
}

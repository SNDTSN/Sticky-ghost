using App.Core.Domain.Entities;
using App.Core.Domain.Repositories;
using Microsoft.Data.Sqlite;

namespace App.Core.Infrastructure.Sqlite;

public sealed class SqliteCategoryRepository : ICategoryRepository
{
    private readonly string _connectionString;

    public SqliteCategoryRepository(string connectionString)
    {
        _connectionString = connectionString;
    }

    public void Save(Category category)
    {
        using var connection = SqliteConnectionHelper.OpenConnection(_connectionString);
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO Category (Id, Name, Color, SortOrder)
            VALUES ($Id, $Name, $Color, $SortOrder)
            ON CONFLICT(Id) DO UPDATE SET
                Name = excluded.Name,
                Color = excluded.Color,
                SortOrder = excluded.SortOrder;
            """;
        command.Parameters.AddWithValue("$Id", category.Id.ToString());
        command.Parameters.AddWithValue("$Name", category.Name);
        command.Parameters.AddWithValue("$Color", category.Color);
        command.Parameters.AddWithValue("$SortOrder", category.SortOrder);
        command.ExecuteNonQuery();
    }

    public void Delete(Guid id)
    {
        using var connection = SqliteConnectionHelper.OpenConnection(_connectionString);
        using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM Category WHERE Id = $Id;";
        command.Parameters.AddWithValue("$Id", id.ToString());
        command.ExecuteNonQuery();
    }

    public IEnumerable<Category> GetAll()
    {
        using var connection = SqliteConnectionHelper.OpenConnection(_connectionString);
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT * FROM Category ORDER BY SortOrder;";

        using var reader = command.ExecuteReader();
        var categories = new List<Category>();
        while (reader.Read())
        {
            categories.Add(new Category
            {
                Id = Guid.Parse(reader.GetString(reader.GetOrdinal("Id"))),
                Name = reader.GetString(reader.GetOrdinal("Name")),
                Color = reader.GetString(reader.GetOrdinal("Color")),
                SortOrder = reader.GetInt32(reader.GetOrdinal("SortOrder")),
            });
        }
        return categories;
    }
}

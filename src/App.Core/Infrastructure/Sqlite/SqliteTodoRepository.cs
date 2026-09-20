using App.Core.Domain.Entities;
using App.Core.Domain.Repositories;
using Microsoft.Data.Sqlite;

namespace App.Core.Infrastructure.Sqlite;

public sealed class SqliteTodoRepository : ITodoRepository
{
    // 요일 비트마스크 순서 (docs/todo-design.md: 월=1,화=2,수=4...)
    private static readonly DayOfWeek[] BitOrder =
    {
        DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday,
        DayOfWeek.Thursday, DayOfWeek.Friday, DayOfWeek.Saturday, DayOfWeek.Sunday,
    };

    private readonly string _connectionString;

    public SqliteTodoRepository(string connectionString)
    {
        _connectionString = connectionString;
    }

    public void Save(TodoItem item)
    {
        using var connection = SqliteConnectionHelper.OpenConnection(_connectionString);
        using var transaction = connection.BeginTransaction();

        using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = """
                INSERT INTO TodoItem
                    (Id, Title, CategoryId, IsImportant, IsUrgent, DueDate, IsCompleted, CreatedAt, CompletedAt,
                     RecurrenceType, RecurrenceInterval, RecurrenceDaysOfWeek, RecurrenceEndDate, NotifiedDueSoon,
                     CompletionCount)
                VALUES
                    ($Id, $Title, $CategoryId, $IsImportant, $IsUrgent, $DueDate, $IsCompleted, $CreatedAt, $CompletedAt,
                     $RecurrenceType, $RecurrenceInterval, $RecurrenceDaysOfWeek, $RecurrenceEndDate, $NotifiedDueSoon,
                     $CompletionCount)
                ON CONFLICT(Id) DO UPDATE SET
                    Title = excluded.Title,
                    CategoryId = excluded.CategoryId,
                    IsImportant = excluded.IsImportant,
                    IsUrgent = excluded.IsUrgent,
                    DueDate = excluded.DueDate,
                    IsCompleted = excluded.IsCompleted,
                    CreatedAt = excluded.CreatedAt,
                    CompletedAt = excluded.CompletedAt,
                    RecurrenceType = excluded.RecurrenceType,
                    RecurrenceInterval = excluded.RecurrenceInterval,
                    RecurrenceDaysOfWeek = excluded.RecurrenceDaysOfWeek,
                    RecurrenceEndDate = excluded.RecurrenceEndDate,
                    NotifiedDueSoon = excluded.NotifiedDueSoon,
                    CompletionCount = excluded.CompletionCount;
                """;

            object daysOfWeekValue = item.Recurrence?.DaysOfWeek is { Count: > 0 } days
                ? EncodeDaysOfWeek(days)
                : DBNull.Value;

            command.Parameters.AddWithValue("$Id", item.Id.ToString());
            command.Parameters.AddWithValue("$Title", item.Title);
            command.Parameters.AddWithValue("$CategoryId", (object?)item.CategoryId?.ToString() ?? DBNull.Value);
            command.Parameters.AddWithValue("$IsImportant", item.IsImportant);
            command.Parameters.AddWithValue("$IsUrgent", item.IsUrgent);
            command.Parameters.AddWithValue("$DueDate", (object?)item.DueDate ?? DBNull.Value);
            command.Parameters.AddWithValue("$IsCompleted", item.IsCompleted);
            command.Parameters.AddWithValue("$CreatedAt", item.CreatedAt);
            command.Parameters.AddWithValue("$CompletedAt", (object?)item.CompletedAt ?? DBNull.Value);
            command.Parameters.AddWithValue("$RecurrenceType", (object?)item.Recurrence?.Type.ToString() ?? DBNull.Value);
            command.Parameters.AddWithValue("$RecurrenceInterval", (object?)item.Recurrence?.Interval ?? DBNull.Value);
            command.Parameters.AddWithValue("$RecurrenceDaysOfWeek", daysOfWeekValue);
            command.Parameters.AddWithValue("$RecurrenceEndDate", (object?)item.Recurrence?.EndDate ?? DBNull.Value);
            command.Parameters.AddWithValue("$NotifiedDueSoon", item.NotifiedDueSoon);
            command.Parameters.AddWithValue("$CompletionCount", item.CompletionCount);

            command.ExecuteNonQuery();
        }

        using (var deleteCommand = connection.CreateCommand())
        {
            deleteCommand.Transaction = transaction;
            deleteCommand.CommandText = "DELETE FROM ChecklistItem WHERE TodoItemId = $TodoItemId;";
            deleteCommand.Parameters.AddWithValue("$TodoItemId", item.Id.ToString());
            deleteCommand.ExecuteNonQuery();
        }

        foreach (var checklistItem in item.ChecklistItems)
        {
            using var insertCommand = connection.CreateCommand();
            insertCommand.Transaction = transaction;
            insertCommand.CommandText = """
                INSERT INTO ChecklistItem (Id, TodoItemId, Text, IsChecked, SortOrder)
                VALUES ($Id, $TodoItemId, $Text, $IsChecked, $SortOrder);
                """;
            insertCommand.Parameters.AddWithValue("$Id", checklistItem.Id.ToString());
            insertCommand.Parameters.AddWithValue("$TodoItemId", item.Id.ToString());
            insertCommand.Parameters.AddWithValue("$Text", checklistItem.Text);
            insertCommand.Parameters.AddWithValue("$IsChecked", checklistItem.IsChecked);
            insertCommand.Parameters.AddWithValue("$SortOrder", checklistItem.SortOrder);
            insertCommand.ExecuteNonQuery();
        }

        transaction.Commit();
    }

    // ChecklistItem/CompletionLog는 TodoItem을 FK ON DELETE CASCADE로 참조하므로(SqliteConnectionHelper가
    // 연결마다 PRAGMA foreign_keys = ON을 걸어둠) 여기서 따로 지울 필요 없이 자동으로 같이 삭제된다.
    public void Delete(Guid id)
    {
        using var connection = SqliteConnectionHelper.OpenConnection(_connectionString);
        using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM TodoItem WHERE Id = $Id;";
        command.Parameters.AddWithValue("$Id", id.ToString());
        command.ExecuteNonQuery();
    }

    public TodoItem? Get(Guid id)
    {
        using var connection = SqliteConnectionHelper.OpenConnection(_connectionString);

        TodoItem? item;
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT * FROM TodoItem WHERE Id = $Id;";
            command.Parameters.AddWithValue("$Id", id.ToString());

            using var reader = command.ExecuteReader();
            item = reader.Read() ? ReadTodoItem(reader) : null;
        }

        if (item is not null)
            item.ChecklistItems = LoadChecklistItems(connection, item.Id);

        return item;
    }

    public IEnumerable<TodoItem> GetActiveWithDueDate()
    {
        using var connection = SqliteConnectionHelper.OpenConnection(_connectionString);

        var items = new List<TodoItem>();
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT * FROM TodoItem WHERE IsCompleted = 0 AND DueDate IS NOT NULL;";
            using var reader = command.ExecuteReader();
            while (reader.Read())
                items.Add(ReadTodoItem(reader));
        }

        AttachChecklistItems(connection, items);

        return items;
    }

    public IEnumerable<TodoItem> GetIncomplete()
    {
        using var connection = SqliteConnectionHelper.OpenConnection(_connectionString);

        var items = new List<TodoItem>();
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT * FROM TodoItem WHERE IsCompleted = 0;";
            using var reader = command.ExecuteReader();
            while (reader.Read())
                items.Add(ReadTodoItem(reader));
        }

        AttachChecklistItems(connection, items);

        return items;
    }

    // 항목마다 별도 쿼리(N+1)로 체크리스트를 불러오는 대신 TodoItemId IN (...) 한 번으로 모아서 가져온다.
    private static void AttachChecklistItems(SqliteConnection connection, List<TodoItem> items)
    {
        if (items.Count == 0)
            return;

        var checklistsByTodoId = LoadChecklistItemsForMany(connection, items.Select(i => i.Id));
        foreach (var item in items)
            item.ChecklistItems = checklistsByTodoId.TryGetValue(item.Id, out var checklist) ? checklist : new List<ChecklistItem>();
    }

    private static Dictionary<Guid, List<ChecklistItem>> LoadChecklistItemsForMany(SqliteConnection connection, IEnumerable<Guid> todoItemIds)
    {
        var ids = todoItemIds.ToList();
        var result = new Dictionary<Guid, List<ChecklistItem>>();
        if (ids.Count == 0)
            return result;

        using var command = connection.CreateCommand();
        var parameterNames = ids.Select((_, i) => $"$id{i}").ToList();
        command.CommandText = $"""
            SELECT Id, TodoItemId, Text, IsChecked, SortOrder
            FROM ChecklistItem
            WHERE TodoItemId IN ({string.Join(", ", parameterNames)})
            ORDER BY TodoItemId, SortOrder;
            """;
        for (var i = 0; i < ids.Count; i++)
            command.Parameters.AddWithValue(parameterNames[i], ids[i].ToString());

        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            var todoItemId = Guid.Parse(reader.GetString(1));
            if (!result.TryGetValue(todoItemId, out var checklist))
            {
                checklist = new List<ChecklistItem>();
                result[todoItemId] = checklist;
            }

            checklist.Add(new ChecklistItem
            {
                Id = Guid.Parse(reader.GetString(0)),
                TodoItemId = todoItemId,
                Text = reader.GetString(2),
                IsChecked = reader.GetBoolean(3),
                SortOrder = reader.GetInt32(4),
            });
        }

        return result;
    }

    private static TodoItem ReadTodoItem(SqliteDataReader reader)
    {
        var recurrenceTypeOrdinal = reader.GetOrdinal("RecurrenceType");
        RecurrenceRule? recurrence = null;
        if (!reader.IsDBNull(recurrenceTypeOrdinal))
        {
            var type = Enum.Parse<RecurrenceType>(reader.GetString(recurrenceTypeOrdinal));
            var interval = reader.GetInt32(reader.GetOrdinal("RecurrenceInterval"));

            var daysOfWeekOrdinal = reader.GetOrdinal("RecurrenceDaysOfWeek");
            var daysOfWeek = reader.IsDBNull(daysOfWeekOrdinal)
                ? null
                : (IReadOnlySet<DayOfWeek>)DecodeDaysOfWeek(reader.GetInt32(daysOfWeekOrdinal));

            var endDateOrdinal = reader.GetOrdinal("RecurrenceEndDate");
            var endDate = reader.IsDBNull(endDateOrdinal) ? (DateTime?)null : reader.GetDateTime(endDateOrdinal);

            recurrence = new RecurrenceRule(type, interval, daysOfWeek, endDate);
        }

        var categoryIdOrdinal = reader.GetOrdinal("CategoryId");
        var dueDateOrdinal = reader.GetOrdinal("DueDate");
        var completedAtOrdinal = reader.GetOrdinal("CompletedAt");

        return new TodoItem
        {
            Id = Guid.Parse(reader.GetString(reader.GetOrdinal("Id"))),
            Title = reader.GetString(reader.GetOrdinal("Title")),
            CategoryId = reader.IsDBNull(categoryIdOrdinal) ? null : Guid.Parse(reader.GetString(categoryIdOrdinal)),
            IsImportant = reader.GetBoolean(reader.GetOrdinal("IsImportant")),
            IsUrgent = reader.GetBoolean(reader.GetOrdinal("IsUrgent")),
            DueDate = reader.IsDBNull(dueDateOrdinal) ? null : reader.GetDateTime(dueDateOrdinal),
            IsCompleted = reader.GetBoolean(reader.GetOrdinal("IsCompleted")),
            CreatedAt = reader.GetDateTime(reader.GetOrdinal("CreatedAt")),
            CompletedAt = reader.IsDBNull(completedAtOrdinal) ? null : reader.GetDateTime(completedAtOrdinal),
            Recurrence = recurrence,
            NotifiedDueSoon = reader.GetBoolean(reader.GetOrdinal("NotifiedDueSoon")),
            CompletionCount = reader.GetInt32(reader.GetOrdinal("CompletionCount")),
        };
    }

    private static List<ChecklistItem> LoadChecklistItems(SqliteConnection connection, Guid todoItemId)
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT Id, TodoItemId, Text, IsChecked, SortOrder
            FROM ChecklistItem
            WHERE TodoItemId = $TodoItemId
            ORDER BY SortOrder;
            """;
        command.Parameters.AddWithValue("$TodoItemId", todoItemId.ToString());

        using var reader = command.ExecuteReader();
        var items = new List<ChecklistItem>();
        while (reader.Read())
        {
            items.Add(new ChecklistItem
            {
                Id = Guid.Parse(reader.GetString(0)),
                TodoItemId = Guid.Parse(reader.GetString(1)),
                Text = reader.GetString(2),
                IsChecked = reader.GetBoolean(3),
                SortOrder = reader.GetInt32(4),
            });
        }
        return items;
    }

    private static int EncodeDaysOfWeek(IReadOnlySet<DayOfWeek> days)
    {
        var mask = 0;
        for (var i = 0; i < BitOrder.Length; i++)
        {
            if (days.Contains(BitOrder[i]))
                mask |= 1 << i;
        }
        return mask;
    }

    private static HashSet<DayOfWeek> DecodeDaysOfWeek(int mask)
    {
        var days = new HashSet<DayOfWeek>();
        for (var i = 0; i < BitOrder.Length; i++)
        {
            if ((mask & (1 << i)) != 0)
                days.Add(BitOrder[i]);
        }
        return days;
    }
}

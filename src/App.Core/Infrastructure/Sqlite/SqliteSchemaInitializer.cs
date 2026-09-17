namespace App.Core.Infrastructure.Sqlite;

/// <summary>
/// docs/todo-design.md의 DDL을 기준으로 스키마를 생성한다.
/// 아직 스키마 변경 이력 관리가 필요 없는 단계라 별도 마이그레이션 도구 없이
/// CREATE TABLE IF NOT EXISTS로만 처리한다.
/// </summary>
public static class SqliteSchemaInitializer
{
    public static void EnsureCreated(string connectionString)
    {
        using var connection = SqliteConnectionHelper.OpenConnection(connectionString);
        using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS Category (
                Id          TEXT PRIMARY KEY,
                Name        TEXT NOT NULL,
                Color       TEXT NOT NULL,
                SortOrder   INTEGER NOT NULL DEFAULT 0
            );

            CREATE TABLE IF NOT EXISTS TodoItem (
                Id                  TEXT PRIMARY KEY,
                Title               TEXT NOT NULL,
                CategoryId          TEXT REFERENCES Category(Id) ON DELETE SET NULL,
                IsImportant         INTEGER NOT NULL DEFAULT 0,
                IsUrgent            INTEGER NOT NULL DEFAULT 0,
                DueDate             TEXT,
                IsCompleted         INTEGER NOT NULL DEFAULT 0,
                CreatedAt           TEXT NOT NULL,
                CompletedAt         TEXT,

                RecurrenceType       TEXT,
                RecurrenceInterval   INTEGER,
                RecurrenceDaysOfWeek INTEGER,
                RecurrenceEndDate    TEXT,

                NotifiedDueSoon      INTEGER NOT NULL DEFAULT 0
            );

            CREATE TABLE IF NOT EXISTS ChecklistItem (
                Id          TEXT PRIMARY KEY,
                TodoItemId  TEXT NOT NULL REFERENCES TodoItem(Id) ON DELETE CASCADE,
                Text        TEXT NOT NULL,
                IsChecked   INTEGER NOT NULL DEFAULT 0,
                SortOrder   INTEGER NOT NULL DEFAULT 0
            );

            CREATE TABLE IF NOT EXISTS CompletionLog (
                Id                TEXT PRIMARY KEY,
                TodoItemId        TEXT NOT NULL REFERENCES TodoItem(Id) ON DELETE CASCADE,
                CompletedOn       TEXT NOT NULL,
                ChecklistSnapshot TEXT
            );

            CREATE TABLE IF NOT EXISTS MemoNote (
                Id          TEXT PRIMARY KEY,
                Content     TEXT NOT NULL DEFAULT '',
                Color       TEXT NOT NULL,
                PositionX   REAL NOT NULL,
                PositionY   REAL NOT NULL,
                Width       REAL NOT NULL,
                Height      REAL NOT NULL,
                IsPinned    INTEGER NOT NULL DEFAULT 0,
                CreatedAt   TEXT NOT NULL,
                UpdatedAt   TEXT NOT NULL
            );

            CREATE INDEX IF NOT EXISTS idx_todo_duedate   ON TodoItem(DueDate) WHERE IsCompleted = 0;
            CREATE INDEX IF NOT EXISTS idx_checklist_todo ON ChecklistItem(TodoItemId);
            CREATE INDEX IF NOT EXISTS idx_log_todo       ON CompletionLog(TodoItemId);
            """;
        command.ExecuteNonQuery();
    }
}

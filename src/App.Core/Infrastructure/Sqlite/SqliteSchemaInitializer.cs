using System;
using Microsoft.Data.Sqlite;

namespace App.Core.Infrastructure.Sqlite;

/// <summary>
/// docs/todo-design.md의 DDL을 기준으로 스키마를 생성한다.
/// 테이블 자체는 CREATE TABLE IF NOT EXISTS로 처리하지만, 이미 배포된 DB에 나중에 컬럼이
/// 추가되는 경우(예: TodoItem.CompletionCount)를 대비해 EnsureColumnExists로 누락된 컬럼만
/// ALTER TABLE ADD COLUMN으로 보충한다. 테이블 구조 자체가 바뀌는(컬럼 삭제/이름변경 등) 수준의
/// 변경까지 다루는 정식 버전 관리 마이그레이션은 아직 없음 — 필요해지면 KNOWN_ISSUES.md #2 참고.
/// </summary>
public static class SqliteSchemaInitializer
{
    public static void EnsureCreated(string connectionString)
    {
        using var connection = SqliteConnectionHelper.OpenConnection(connectionString);

        using (var command = connection.CreateCommand())
        {
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

                NotifiedDueSoon      INTEGER NOT NULL DEFAULT 0,
                CompletionCount      INTEGER NOT NULL DEFAULT 0
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

        // TodoItem 테이블이 CompletionCount 추가 이전 스키마로 이미 존재하는 경우
        // (CREATE TABLE IF NOT EXISTS는 이럴 때 아무 것도 안 하므로) 컬럼만 보충한다.
        EnsureColumnExists(connection, "TodoItem", "CompletionCount", "INTEGER NOT NULL DEFAULT 0");
    }

    private static void EnsureColumnExists(SqliteConnection connection, string table, string column, string columnDefinition)
    {
        var columnExists = false;
        using (var checkCommand = connection.CreateCommand())
        {
            checkCommand.CommandText = $"PRAGMA table_info({table});";
            using var reader = checkCommand.ExecuteReader();
            var nameOrdinal = reader.GetOrdinal("name");
            while (reader.Read())
            {
                if (string.Equals(reader.GetString(nameOrdinal), column, StringComparison.OrdinalIgnoreCase))
                {
                    columnExists = true;
                    break;
                }
            }
        }

        if (columnExists)
            return;

        using var alterCommand = connection.CreateCommand();
        alterCommand.CommandText = $"ALTER TABLE {table} ADD COLUMN {column} {columnDefinition};";
        alterCommand.ExecuteNonQuery();
    }
}

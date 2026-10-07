using Microsoft.Data.Sqlite;
using Pixel.Core.Brain;

namespace Pixel.Core.Storage;

public sealed record ConversationSummary(long Id, DateTimeOffset StartedAt, DateTimeOffset? EndedAt, string Title, int MessageCount);

public sealed record StoredMessage(long Id, long ConversationId, ChatRole Role, string Content, DateTimeOffset At);

/// <summary>
/// Local chat history in SQLite (%APPDATA%\Pixel\pixel.db). Only the user's typed questions and Pixel's answers
/// are stored — never screen text, prompts or tool traffic. Messages are written as they happen.
/// </summary>
public sealed class HistoryStore : IDisposable
{
    private const int TitleLength = 60;
    private readonly SqliteConnection _db;

    public HistoryStore(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        _db = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString());
        _db.Open();
        Exec("""
            PRAGMA journal_mode = WAL;
            PRAGMA foreign_keys = ON;
            CREATE TABLE IF NOT EXISTS conversations (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                started_at TEXT NOT NULL,
                ended_at TEXT,
                title TEXT NOT NULL DEFAULT ''
            );
            CREATE TABLE IF NOT EXISTS messages (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                conversation_id INTEGER NOT NULL REFERENCES conversations(id) ON DELETE CASCADE,
                role TEXT NOT NULL,
                content TEXT NOT NULL,
                at TEXT NOT NULL
            );
            CREATE INDEX IF NOT EXISTS ix_messages_conversation ON messages(conversation_id, id);
            """);
    }

    public static string DefaultPath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Pixel", "pixel.db");

    public long Start(DateTimeOffset at)
    {
        using var cmd = Command("INSERT INTO conversations (started_at) VALUES ($at); SELECT last_insert_rowid();", ("$at", at.ToString("O")));
        return (long)cmd.ExecuteScalar()!;
    }

    public void Append(long conversationId, ChatRole role, string content, DateTimeOffset at)
    {
        if (role is not (ChatRole.User or ChatRole.Assistant) || string.IsNullOrWhiteSpace(content)) return;
        using (var cmd = Command("INSERT INTO messages (conversation_id, role, content, at) VALUES ($c, $r, $t, $at);",
                   ("$c", conversationId), ("$r", role.ToString()), ("$t", content), ("$at", at.ToString("O"))))
            cmd.ExecuteNonQuery();

        if (role == ChatRole.User)
        {
            var title = content.ReplaceLineEndings(" ").Trim();
            if (title.Length > TitleLength) title = title[..TitleLength] + "…";
            using var t = Command("UPDATE conversations SET title = $t WHERE id = $c AND title = '';", ("$t", title), ("$c", conversationId));
            t.ExecuteNonQuery();
        }
    }

    /// <summary>Marks the conversation finished; conversations with no messages are dropped.</summary>
    public void End(long conversationId, DateTimeOffset at)
    {
        using var cmd = Command("""
            UPDATE conversations SET ended_at = $at WHERE id = $c;
            DELETE FROM conversations WHERE id = $c AND NOT EXISTS (SELECT 1 FROM messages WHERE conversation_id = $c);
            """, ("$at", at.ToString("O")), ("$c", conversationId));
        cmd.ExecuteNonQuery();
    }

    public IReadOnlyList<ConversationSummary> List(int limit = 500) =>
        Summaries("WHERE EXISTS (SELECT 1 FROM messages m WHERE m.conversation_id = c.id)", limit);

    public IReadOnlyList<ConversationSummary> Search(string text, int limit = 500) =>
        Summaries("WHERE EXISTS (SELECT 1 FROM messages m WHERE m.conversation_id = c.id AND m.content LIKE $q ESCAPE '\\')",
            limit, ("$q", "%" + EscapeLike(text) + "%"));

    public IReadOnlyList<StoredMessage> Messages(long conversationId)
    {
        using var cmd = Command("SELECT id, conversation_id, role, content, at FROM messages WHERE conversation_id = $c ORDER BY id;", ("$c", conversationId));
        using var r = cmd.ExecuteReader();
        var list = new List<StoredMessage>();
        while (r.Read())
            list.Add(new StoredMessage(r.GetInt64(0), r.GetInt64(1), Enum.Parse<ChatRole>(r.GetString(2)), r.GetString(3), DateTimeOffset.Parse(r.GetString(4))));
        return list;
    }

    public void Delete(long conversationId)
    {
        using var cmd = Command("DELETE FROM conversations WHERE id = $c;", ("$c", conversationId));
        cmd.ExecuteNonQuery();
    }

    public void DeleteAll() => Exec("DELETE FROM messages; DELETE FROM conversations;");

    private IReadOnlyList<ConversationSummary> Summaries(string where, int limit, params (string, object)[] args)
    {
        using var cmd = Command($"""
            SELECT c.id, c.started_at, c.ended_at, c.title,
                   (SELECT COUNT(*) FROM messages m WHERE m.conversation_id = c.id)
            FROM conversations c {where}
            ORDER BY c.started_at DESC, c.id DESC
            LIMIT {limit};
            """, args);
        using var r = cmd.ExecuteReader();
        var list = new List<ConversationSummary>();
        while (r.Read())
        {
            list.Add(new ConversationSummary(
                r.GetInt64(0),
                DateTimeOffset.Parse(r.GetString(1)),
                r.IsDBNull(2) ? null : DateTimeOffset.Parse(r.GetString(2)),
                r.GetString(3),
                r.GetInt32(4)));
        }
        return list;
    }

    private static string EscapeLike(string s) => s.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");

    private SqliteCommand Command(string sql, params (string Name, object Value)[] args)
    {
        var cmd = _db.CreateCommand();
        cmd.CommandText = sql;
        foreach (var (n, v) in args) cmd.Parameters.AddWithValue(n, v);
        return cmd;
    }

    private void Exec(string sql)
    {
        using var cmd = Command(sql);
        cmd.ExecuteNonQuery();
    }

    public void Dispose() => _db.Dispose();
}

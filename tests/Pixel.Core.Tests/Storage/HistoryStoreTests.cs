using Pixel.Core.Brain;
using Pixel.Core.Storage;

namespace Pixel.Core.Tests.Storage;

public sealed class HistoryStoreTests : IDisposable
{
    private static readonly DateTimeOffset T = new(2026, 10, 7, 10, 0, 0, TimeSpan.Zero);
    private readonly string _dir = Directory.CreateTempSubdirectory("pixel-history-").FullName;
    private readonly HistoryStore _store;

    public HistoryStoreTests() => _store = new HistoryStore(Path.Combine(_dir, "pixel.db"));

    public void Dispose()
    {
        _store.Dispose();
        Directory.Delete(_dir, true);
    }

    private long Conversation(string first, DateTimeOffset at, params string[] replies)
    {
        var id = _store.Start(at);
        _store.Append(id, ChatRole.User, first, at);
        foreach (var r in replies) _store.Append(id, ChatRole.Assistant, r, at.AddSeconds(5));
        _store.End(id, at.AddMinutes(1));
        return id;
    }

    [Fact]
    public void Saved_conversation_is_listed_with_title_and_count()
    {
        Conversation("How do I reverse a list in Python?", T, "Use reversed() or [::-1].");
        var c = Assert.Single(_store.List());
        Assert.Equal("How do I reverse a list in Python?", c.Title);
        Assert.Equal(2, c.MessageCount);
    }

    [Fact]
    public void Long_first_question_gives_a_short_title()
    {
        Conversation(new string('a', 300), T);
        Assert.True(_store.List()[0].Title.Length <= 61);
    }

    [Fact]
    public void Newest_conversation_comes_first()
    {
        Conversation("older", T);
        Conversation("newer", T.AddHours(1));
        Assert.Equal(["newer", "older"], _store.List().Select(c => c.Title).ToArray());
    }

    [Fact]
    public void Search_matches_any_message_case_insensitively()
    {
        Conversation("python question", T, "Use a LIST comprehension");
        Conversation("css question", T.AddHours(1), "Use flexbox");
        Assert.Equal("python question", Assert.Single(_store.Search("list comp")).Title);
    }

    [Fact]
    public void Messages_come_back_in_order()
    {
        var id = Conversation("q", T, "a1", "a2");
        Assert.Equal(["q", "a1", "a2"], _store.Messages(id).Select(m => m.Content).ToArray());
    }

    [Fact]
    public void Delete_removes_conversation_and_messages()
    {
        var id = Conversation("bye", T, "ok");
        _store.Delete(id);
        Assert.Empty(_store.List());
        Assert.Empty(_store.Messages(id));
    }

    [Fact]
    public void Empty_conversation_is_not_kept()
    {
        var id = _store.Start(T);
        _store.End(id, T);
        Assert.Empty(_store.List());
    }

    [Fact]
    public void System_and_tool_messages_are_not_stored()
    {
        var id = _store.Start(T);
        _store.Append(id, ChatRole.User, "hi", T);
        _store.Append(id, ChatRole.Tool, "Reminder set", T);
        _store.Append(id, ChatRole.System, "persona", T);
        Assert.Single(_store.Messages(id));
    }

    [Fact]
    public void Data_survives_reopening_the_database()
    {
        Conversation("persist me", T);
        _store.Dispose();
        using var reopened = new HistoryStore(Path.Combine(_dir, "pixel.db"));
        Assert.Equal("persist me", Assert.Single(reopened.List()).Title);
    }

    [Fact]
    public void Delete_all_clears_everything()
    {
        Conversation("one", T);
        Conversation("two", T);
        _store.DeleteAll();
        Assert.Empty(_store.List());
    }
}

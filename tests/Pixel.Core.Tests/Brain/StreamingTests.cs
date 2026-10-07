using Pixel.Core.Brain;

namespace Pixel.Core.Tests.Brain;

public class SseParserTests
{
    [Fact]
    public void Parses_content_deltas_split_across_chunks()
    {
        var p = new SseParser();
        var deltas = p.Feed("data: {\"choices\":[{\"delta\":{\"content\":\"Hel\"}}]}\n\ndata: {\"choi")
            .Concat(p.Feed("ces\":[{\"delta\":{\"content\":\"lo\"}}]}\n\n"))
            .ToList();
        Assert.Equal("Hello", string.Concat(deltas.Select(d => d.Content)));
    }

    [Fact]
    public void Done_marker_ends_the_stream()
    {
        var deltas = new SseParser().Feed("data: [DONE]\n\n").ToList();
        Assert.True(Assert.Single(deltas).Done);
    }

    [Fact]
    public void Tool_call_fragments_are_reported_with_index()
    {
        var p = new SseParser();
        var d = p.Feed(
            "data: {\"choices\":[{\"delta\":{\"tool_calls\":[{\"index\":0,\"id\":\"call_1\",\"function\":{\"name\":\"set_reminder\",\"arguments\":\"{\\\"min\"}}]}}]}\n\n" +
            "data: {\"choices\":[{\"delta\":{\"tool_calls\":[{\"index\":0,\"function\":{\"arguments\":\"utes\\\":5}\"}}]}}]}\n\n").ToList();

        var acc = new ToolCallAccumulator();
        foreach (var t in d.SelectMany(x => x.ToolCalls)) acc.Add(t);
        var call = Assert.Single(acc.Build());
        Assert.Equal("call_1", call.Id);
        Assert.Equal("set_reminder", call.Name);
        Assert.Equal("{\"minutes\":5}", call.ArgumentsJson);
    }

    [Fact]
    public void Ignores_keepalive_comments_and_blank_lines()
    {
        Assert.Empty(new SseParser().Feed(": ping\n\n\n"));
    }
}

public class InlineToolCallFilterTests
{
    [Fact]
    public void Plain_text_passes_through()
    {
        var f = new InlineToolCallFilter();
        Assert.Equal("Hello world", f.Push("Hello ") + f.Push("world") + f.Flush());
        Assert.Empty(f.Calls);
    }

    [Fact]
    public void Tool_call_tags_are_removed_and_parsed_even_when_split()
    {
        var f = new InlineToolCallFilter();
        var visible = f.Push("Sure! <tool") + f.Push("_call>\n{\"name\": \"copy_to_clipboard\", \"arguments\": {\"text\": \"hi\"}}\n</tool_")
            + f.Push("call> Done.") + f.Flush();
        Assert.Equal("Sure!  Done.", visible);
        var call = Assert.Single(f.Calls);
        Assert.Equal("copy_to_clipboard", call.Name);
        Assert.Contains("\"text\"", call.ArgumentsJson);
    }

    [Fact]
    public void A_lone_angle_bracket_is_not_swallowed()
    {
        var f = new InlineToolCallFilter();
        Assert.Equal("if a < b then", f.Push("if a <") + f.Push(" b then") + f.Flush());
    }
}

using Api.Services;
using Xunit;

namespace Api.Tests;

public class ChatResponseParserTests
{
    private static readonly ChatResponseParser Parser = new();

    [Fact]
    public void Parse_ReturnsRawString_WhenInputIsPlainText()
        => Assert.Equal("Hello world", Parser.Parse("Hello world"));

    [Fact]
    public void Parse_ReturnsEmpty_WhenInputIsEmpty()
        => Assert.Equal(string.Empty, Parser.Parse(string.Empty));

    [Fact]
    public void Parse_ExtractsText_FromTextProperty()
        => Assert.Equal("Answer", Parser.Parse("{\"text\":\"Answer\"}"));

    [Fact]
    public void Parse_ReturnsEmpty_WhenTextPropertyIsEmpty()
        => Assert.Equal(string.Empty, Parser.Parse("{\"text\":\"\"}"));

    [Fact]
    public void Parse_ExtractsText_FromAnswerProperty()
        => Assert.Equal("Direct answer", Parser.Parse("{\"answer\":\"Direct answer\"}"));

    [Fact]
    public void Parse_ExtractsNestedText_FromAnswerWrappingJsonString()
        => Assert.Equal("Inner text", Parser.Parse("{\"answer\":\"{\\\"text\\\":\\\"Inner text\\\"}\"}"));

    [Fact]
    public void Parse_ExtractsText_FromChoicesTextField()
        => Assert.Equal("Choice answer", Parser.Parse("{\"choices\":[{\"text\":\"Choice answer\"}]}"));

    [Fact]
    public void Parse_ExtractsText_FromChoicesMessageContent()
        => Assert.Equal("Chat content", Parser.Parse("{\"choices\":[{\"message\":{\"content\":\"Chat content\"}}]}"));

    [Fact]
    public void Parse_ExtractsText_FromChatApiMessageContent()
        => Assert.Equal("Chat API answer", Parser.Parse("{\"message\":{\"role\":\"assistant\",\"content\":\"Chat API answer\"}}"));

    [Fact]
    public void Parse_NormalisesFallback_WhenChatApiMessageContainsFallbackPhrase()
    {
        var input = $"{{\"message\":{{\"role\":\"assistant\",\"content\":\"{ChatResponseParser.Fallback}\"}}}}";
        Assert.Equal(ChatResponseParser.Fallback, Parser.Parse(input));
    }

    [Fact]
    public void Parse_ReturnsRaw_WhenChoicesArrayIsEmpty()
    {
        const string input = "{\"choices\":[]}";
        Assert.Equal(input, Parser.Parse(input));
    }

    [Fact]
    public void Parse_ConcatenatesResponse_FromJsonLines()
        => Assert.Equal("AB", Parser.Parse("{\"response\":\"A\"}\n{\"response\":\"B\"}"));

    [Fact]
    public void Parse_ConcatenatesText_FromJsonLinesWithTextField()
        => Assert.Equal("XY", Parser.Parse("{\"text\":\"X\"}\n{\"text\":\"Y\"}"));

    [Fact]
    public void Parse_ReturnsFallback_WhenJsonLineContainsFallbackPhrase()
    {
        var line = $"{{\"response\":\"{ChatResponseParser.Fallback}\"}}";
        Assert.Equal(ChatResponseParser.Fallback, Parser.Parse(line));
    }

    [Fact]
    public void Parse_ReturnsRaw_WhenJsonLinesHaveNoKnownField()
    {
        const string input = "{\"unknown\":\"value\"}\n{\"other\":\"data\"}";
        Assert.Equal(input, Parser.Parse(input));
    }

    [Fact]
    public void Parse_NormalisesFallback_WhenTextFieldContainsFallbackPhrase()
    {
        var input = $"{{\"text\":\"{ChatResponseParser.Fallback}\"}}";
        Assert.Equal(ChatResponseParser.Fallback, Parser.Parse(input));
    }

    [Fact]
    public void Parse_NormalisesFallback_WhenAnswerFieldContainsFallbackPhrase()
    {
        var input = $"{{\"answer\":\"{ChatResponseParser.Fallback}\"}}";
        Assert.Equal(ChatResponseParser.Fallback, Parser.Parse(input));
    }

    [Fact]
    public void Parse_ReturnsRaw_WhenInputIsMalformedJson()
        => Assert.Equal("{broken json", Parser.Parse("{broken json"));

    [Fact]
    public void Parse_HandlesMultilineJsonLines_WithBlankLines()
        => Assert.Equal("Hello", Parser.Parse("{\"response\":\"Hello\"}\n\n"));
}

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

    // ── Hallucinated-refusal guardrail tests ────────────────────────────────

    [Theory]
    [InlineData("I couldn't find any information on Pedro Duarte's personal preferences or hobbies, including whether he likes potatoes.")]
    [InlineData("I could not find information about that topic on this website.")]
    [InlineData("I can't find any information about Pedro's favourite food.")]
    [InlineData("I don't have enough information to answer that.")]
    [InlineData("I don't have enough context to respond.")]
    [InlineData("I don't have information about that.")]
    [InlineData("That is not mentioned on the website content.")]
    [InlineData("There is no information about potatoes on this website.")]
    [InlineData("There is no mention regarding that topic.")]
    [InlineData("I shouldn't have asked about food without having more context.")]
    [InlineData("My previous response was about something else.")]
    [InlineData("That is not available in the knowledge base.")]
    [InlineData("That is not found on this website content.")]
    [InlineData("However, it does not explicitly state that he programs in C#.")]
    [InlineData("It is not explicitly mentioned in the provided information.")]
    [InlineData("There is no explicit mention of that topic.")]
    [InlineData("The website does not explicitly say whether he works out.")]
    public void Parse_NormalisesHallucinatedRefusal_ToCanonicalFallback(string hallucinatedRefusal)
    {
        var input = $"{{\"message\":{{\"role\":\"assistant\",\"content\":\"{hallucinatedRefusal.Replace("\"", "\\\"")}\"}}}}";
        Assert.Equal(ChatResponseParser.Fallback, Parser.Parse(input));
    }

    // ── Prompt-leakage stripping tests ──────────────────────────────────────

    [Theory]
    [InlineData(
        "According to the WEBSITE CONTENT, Pedro is a Senior Software Engineer.",
        "Pedro is a Senior Software Engineer.")]
    [InlineData(
        "Based on the reference information, Pedro has 5+ years of experience.",
        "Pedro has 5+ years of experience.")]
    [InlineData(
        "The WEBSITE CONTENT says Pedro is based in Lisbon.",
        "Pedro is based in Lisbon.")]
    [InlineData(
        "Based on the provided context, his skills include C# and .NET.",
        "His skills include C# and .NET.")]
    [InlineData(
        "According to the knowledge base, Pedro enjoys fishing.",
        "Pedro enjoys fishing.")]
    public void Parse_StripsPromptLeakage_KeepsFactualContent(string leakyAnswer, string expected)
    {
        var input = $"{{\"message\":{{\"role\":\"assistant\",\"content\":\"{leakyAnswer.Replace("\"", "\\\"")}\"}}}}";
        Assert.Equal(expected, Parser.Parse(input));
    }

    [Theory]
    [InlineData("Pedro is a Senior Software Engineer based in Lisbon.")]
    [InlineData("Pedro has 5+ years of experience in backend systems.")]
    [InlineData("His hobbies include fishing, motorcycle riding, and cooking.")]
    [InlineData("Yes! Pedro programs in C# and has 5+ years of experience with .NET.")]
    public void Parse_DoesNotReplace_LegitimateAnswers(string legitimateAnswer)
    {
        var input = $"{{\"message\":{{\"role\":\"assistant\",\"content\":\"{legitimateAnswer}\"}}}}";
        Assert.Equal(legitimateAnswer, Parser.Parse(input));
    }
}

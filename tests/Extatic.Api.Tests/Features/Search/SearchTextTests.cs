using System.Text.Json;
using Extatic.Api.Features.Search;

namespace Extatic.Api.Tests.Features.Search;

public class SearchTextTests
{
    [Theory]
    [InlineData("<p>Hello <b>world</b></p>", "Hello world")]
    [InlineData("a < b and c > d", "a < b and c > d")]
    [InlineData("x<script>alert('no')</script>y", "x y")]
    [InlineData("<style>.a{color:red}</style>text", "text")]
    [InlineData("<!-- hidden -->shown", "shown")]
    [InlineData("<a title=\"x>y\" href='/'>link</a>", "link")]
    [InlineData("Fish &amp; chips &lt;3", "Fish & chips <3")]
    [InlineData("<SCRIPT>bad()</SCRIPT>ok", "ok")]
    [InlineData("unterminated <script>never closed", "unterminated")]
    [InlineData("tabs\tand\nnewlines\u0000nul", "tabs and newlines nul")]
    public void StripHtml_ProducesPlainText(string html, string expected)
        => Assert.Equal(expected, SearchText.StripHtml(html));

    [Fact]
    public void Clean_RemovesHighlightSentinelsAndUnpairedSurrogates()
    {
        var cleaned = SearchText.Clean($"a{SearchText.MarkStart}b{SearchText.MarkEnd}c \uD800 😀");
        Assert.Equal("abc � 😀", cleaned);
    }

    [Fact]
    public void RenderSnippet_EscapesEverythingButMarks()
    {
        var headline = $"<img src=x onerror=alert(1)> & \"q\" {SearchText.MarkStart}hit{SearchText.MarkEnd} </mark><script>";
        var html = SearchText.RenderSnippet(headline);
        Assert.Equal("&lt;img src=x onerror=alert(1)&gt; &amp; &quot;q&quot; <mark>hit</mark> &lt;/mark&gt;&lt;script&gt;", html);
    }

    [Fact]
    public void RenderSnippet_ClosesAnUnbalancedMark()
        => Assert.Equal("<mark>a</mark>", SearchText.RenderSnippet($"{SearchText.MarkStart}a"));

    [Theory]
    [InlineData("pre", "'pre':*")]
    [InlineData("getting sta", "'getting' & 'sta':*")]
    [InlineData("'); DROP TABLE x; --", "'drop' & 'table' & 'x':*")]
    [InlineData("a&b|c!:*", "'a' & 'b' & 'c':*")]
    [InlineData("Café crème", "'café' & 'crème':*")]
    public void BuildPrefixQuery_KeepsOnlyWordCharacters(string input, string expected)
        => Assert.Equal(expected, SearchText.BuildPrefixQuery(input));

    [Theory]
    [InlineData("😀")]
    [InlineData("&|!:*()'\"")]
    public void BuildPrefixQuery_ReturnsNullWithoutWords(string input)
        => Assert.Null(SearchText.BuildPrefixQuery(input));

    [Theory]
    [InlineData("english", true)]
    [InlineData("simple", true)]
    [InlineData("french", true)]
    [InlineData("English", false)]
    [InlineData("pg_catalog.english", false)]
    [InlineData("english'; drop", false)]
    [InlineData(null, false)]
    public void Languages_AreAllowlisted(string? language, bool valid)
        => Assert.Equal(valid, SearchLanguages.IsValid(language));

    private static readonly JsonElement Data = JsonSerializer.SerializeToElement(new Dictionary<string, object?>
    {
        ["title"] = "Hello",
        ["paragraphs"] = new object?[] { "one", "two", new[] { "three" }, 4, null },
        ["nested"] = new { deep = new { text = "deep text" } },
        ["a/b"] = "slash",
        ["m~n"] = "tilde",
        ["count"] = 3,
        ["slug"] = "hello world/x",
    });

    [Theory]
    [InlineData("/title", "Hello")]
    [InlineData("/paragraphs", "one two three")]
    [InlineData("/paragraphs/1", "two")]
    [InlineData("/nested/deep/text", "deep text")]
    [InlineData("/a~1b", "slash")]
    [InlineData("/m~0n", "tilde")]
    [InlineData("/missing", "")]
    [InlineData("/title/deeper", "")]
    [InlineData("/paragraphs/9", "")]
    [InlineData("/paragraphs/01", "")]
    [InlineData("/count", "")]
    [InlineData("title", "")]
    public void Pointers_ExtractStringsAndArrays(string pointer, string expected)
        => Assert.Equal(expected, ItemTextExtractor.Text(Data, pointer));

    [Fact]
    public void UrlTemplate_ExpandsFieldsAndIdWithEncoding()
    {
        var id = Guid.NewGuid();
        Assert.Equal("/blog/hello%20world%2Fx", ItemTextExtractor.ExpandUrl("/blog/{slug}", id, Data));
        Assert.Equal($"/items/{id}?n=3", ItemTextExtractor.ExpandUrl("/items/{id}?n={count}", id, Data));
        Assert.Null(ItemTextExtractor.ExpandUrl("/blog/{missing}", id, Data));
        Assert.Null(ItemTextExtractor.ExpandUrl("/blog/{nested}", id, Data));
        Assert.Null(ItemTextExtractor.ExpandUrl(null, id, Data));
    }

    [Theory]
    [InlineData("/blog/{slug}", true)]
    [InlineData("https://example.com/p/{id}", true)]
    [InlineData("javascript:alert(1)//{id}", false)]
    [InlineData("//evil.example/{id}", false)]
    [InlineData("/blog/{sl ug}", false)]
    [InlineData("/blog/{", false)]
    [InlineData("blog/{slug}", false)]
    public void UrlTemplate_Validation(string template, bool valid)
        => Assert.Equal(valid, ItemTextExtractor.IsValidUrlTemplate(template));
}

using System.Text.RegularExpressions;
using Extatic.Api.Features.Search;
using Extatic.Api.Services;
using Microsoft.EntityFrameworkCore;
using static Extatic.Api.Tests.Features.Search.SearchTestHelper;

namespace Extatic.Api.Tests.Features.Search;

public class SearchQueryTests(ApiTestFixture fixture) : IClassFixture<ApiTestFixture>
{
    [Fact]
    public async Task TitleMatch_RanksAboveBodyMatch_EvenWhenBodyMatchIsNewer()
    {
        var (_, app, _) = await SeedAppAsync(fixture);
        await PushAsync(fixture, app.Id,
            Page("body", "Release notes", "We migrated the cluster to kubernetes last week.", publishedAt: DateTime.UtcNow),
            Page("title", "Kubernetes guide", "An introduction for new engineers.",
                publishedAt: DateTime.UtcNow.AddYears(-2)));

        var result = await SearchAsync(fixture, app.Id, "kubernetes");

        Assert.Equal(["Kubernetes guide", "Release notes"], result.Items.Select(i => i.Title));
        Assert.True(result.Items[0].Score > result.Items[1].Score);
        Assert.Equal(2, result.Total);
    }

    [Fact]
    public async Task Recency_BreaksTiesBetweenEquallyRelevantDocuments()
    {
        var (_, app, _) = await SeedAppAsync(fixture);
        await PushAsync(fixture, app.Id,
            Page("old", "Weekly update", "status", publishedAt: DateTime.UtcNow.AddYears(-1)),
            Page("new", "Weekly update", "status", publishedAt: DateTime.UtcNow.AddDays(-1)));

        var result = await SearchAsync(fixture, app.Id, "weekly");

        Assert.Equal(["new", "old"], result.Items.Select(i => i.Id));
    }

    [Fact]
    public async Task WebsearchSyntax_SupportsPhrasesExclusionAndOr()
    {
        var (_, app, _) = await SeedAppAsync(fixture);
        await PushAsync(fixture, app.Id,
            Page("1", "Apple pie recipe", "Bake the apple pie for an hour."),
            Page("2", "Apple tart", "A French classic."),
            Page("3", "Pie chart basics", "Charts for reports."),
            Page("4", "Apple with pie", "Pie first, then apple."));

        Assert.Equal(["Apple pie recipe", "Apple tart", "Apple with pie"],
            (await TitlesAsync(fixture, app.Id, "apple")).Order());
        Assert.Equal(["Apple tart"], await TitlesAsync(fixture, app.Id, "apple -pie"));
        Assert.Equal(["Apple pie recipe"], await TitlesAsync(fixture, app.Id, "\"apple pie\""));
        Assert.Equal(["Apple tart", "Pie chart basics"],
            (await TitlesAsync(fixture, app.Id, "tart or chart")).Order());
        // Stemming: "baking" finds "Bake".
        Assert.Equal(["Apple pie recipe"], await TitlesAsync(fixture, app.Id, "baking"));
    }

    public static TheoryData<string> HostileQueries =>
    [
        "'); DROP TABLE search_documents; --",
        "&|!:*",
        "\"unbalanced quote",
        "unbalanced quote\"",
        "((( ))) <-> <2>",
        "a:* | !b & c",
        "😀🔥",
        "-",
        "\"",
        "\\",
        "x\u0000y",
        "\uD800 lone surrogate",
        "%_%",
        "' OR 1=1 --",
        "$1 @q :q",
        "or and not",
    ];

    [Theory]
    [MemberData(nameof(HostileQueries))]
    public async Task HostileInput_NeverErrors(string query)
    {
        var (_, app, _) = await SeedAppAsync(fixture);
        await PushAsync(fixture, app.Id, Page("p", "Drop table guide", "Search documents and quotes."));

        var result = await SearchAsync(fixture, app.Id, query, log: true);
        Assert.NotNull(result);
        await using var db = fixture.CreateContext();
        var suggest = await new SearchQueryService(db).SuggestAsync(app.Id, query);
        Assert.NotNull(suggest);
        Assert.Equal(1, await db.SearchDocuments.CountAsync(d => d.AppId == app.Id));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\u0000\t")]
    public async Task EmptyQuery_Is400(string? query)
    {
        var (_, app, _) = await SeedAppAsync(fixture);
        await Assert.ThrowsAsync<BadRequestException>(() => SearchAsync(fixture, app.Id, query));
        await using var db = fixture.CreateContext();
        await Assert.ThrowsAsync<BadRequestException>(() => new SearchQueryService(db).SuggestAsync(app.Id, query));
    }

    [Fact]
    public async Task QueryLongerThan200Chars_Is400_AndPagingIsValidated()
    {
        var (_, app, _) = await SeedAppAsync(fixture);
        await SearchAsync(fixture, app.Id, new string('a', 200));
        await Assert.ThrowsAsync<BadRequestException>(() => SearchAsync(fixture, app.Id, new string('a', 201)));
        await Assert.ThrowsAsync<BadRequestException>(() => SearchAsync(fixture, app.Id, "a", pageSize: 51));
        await Assert.ThrowsAsync<BadRequestException>(() => SearchAsync(fixture, app.Id, "a", page: 0));
        await Assert.ThrowsAsync<BadRequestException>(() => SearchAsync(fixture, app.Id, "a", source: "secret"));
    }

    [Fact]
    public async Task Filters_SectionTagsSourceAndCollection()
    {
        var (_, app, collection) = await SeedAppAsync(fixture);
        await PushAsync(fixture, app.Id,
            Page("b1", "Rust ownership", "rust memory", section: "blog", tags: ["rust", "Memory"]),
            Page("b2", "Rust async", "rust futures", section: "blog", tags: ["rust"]),
            Page("d1", "Rust install", "rust toolchain", section: "docs", tags: ["rust", "setup"]));
        await ConfigureCollectionAsync(fixture, app.Id, collection.Slug);
        await using (var db = fixture.CreateContext())
            await ItemService(fixture, db).CreateAsync(collection.Id, null,
                JsonOf(new { title = "Rust meetup notes", body = "rust community" }), null, app.Id);

        Assert.Equal(4, (await TitlesAsync(fixture, app.Id, "rust")).Count);
        Assert.Equal(["Rust async", "Rust ownership"], (await TitlesAsync(fixture, app.Id, "rust", section: "blog")).Order());
        Assert.Equal(["Rust ownership"], await TitlesAsync(fixture, app.Id, "rust", tags: "rust,memory"));
        Assert.Equal(["Rust ownership"], await TitlesAsync(fixture, app.Id, "rust", tags: " MEMORY "));
        Assert.Empty(await TitlesAsync(fixture, app.Id, "rust", tags: "memory,setup"));
        Assert.Equal(["Rust meetup notes"], await TitlesAsync(fixture, app.Id, "rust", source: "item"));
        Assert.Equal(3, (await TitlesAsync(fixture, app.Id, "rust", source: "page")).Count);
        Assert.Equal(["Rust meetup notes"], await TitlesAsync(fixture, app.Id, "rust", collection: collection.Slug));
        Assert.Empty(await TitlesAsync(fixture, app.Id, "rust", collection: "no-such-collection"));

        var hit = (await SearchAsync(fixture, app.Id, "meetup")).Items.Single();
        Assert.Equal("item", hit.Source);
        Assert.Equal(collection.Slug, hit.Collection);
        Assert.NotNull(hit.ItemId);
    }

    [Fact]
    public async Task Paging_ReturnsStableSlicesAndTotal()
    {
        var (_, app, _) = await SeedAppAsync(fixture);
        await PushAsync(fixture, app.Id, Enumerable.Range(1, 12)
            .Select(i => Page($"p{i}", $"Lesson {i}", "learning content", publishedAt: DateTime.UtcNow.AddDays(-i)))
            .ToArray());

        var pages = new List<string>();
        for (var p = 1; p <= 3; p++)
        {
            var result = await SearchAsync(fixture, app.Id, "learning", page: p, pageSize: 5);
            Assert.Equal(12, result.Total);
            pages.AddRange(result.Items.Select(i => i.Id));
        }
        Assert.Equal(12, pages.Distinct().Count());
        Assert.Equal("p1", pages[0]);

        var beyond = await SearchAsync(fixture, app.Id, "learning", page: 4, pageSize: 5);
        Assert.Empty(beyond.Items);
        Assert.Equal(12, beyond.Total);
    }

    [Fact]
    public async Task Snippet_IsEscapedText_WithOnlyMarkTags()
    {
        var (_, app, _) = await SeedAppAsync(fixture);
        await PushAsync(fixture, app.Id,
            Page("x", "Escaping", "if 5 &lt; 6 &amp; \"quoted\" then findme &lt;script&gt;alert(1)&lt;/script&gt; end"));
        // Bypass the HTML stripper entirely: raw markup straight into the stored body.
        await using (var db = fixture.CreateContext())
            await db.SearchDocuments.Where(d => d.AppId == app.Id && d.ExternalId == "x")
                .ExecuteUpdateAsync(s => s.SetProperty(d => d.Body,
                    "if 5 < 6 & \"quoted\" then findme <script>alert(1)</script> <img src=x onerror=alert(2)> 'single' end"));

        var hit = (await SearchAsync(fixture, app.Id, "findme")).Items.Single();

        Assert.Contains("<mark>findme</mark>", hit.Snippet);
        Assert.Contains("&quot;", hit.Snippet);
        Assert.Contains("&lt;img src=x onerror=alert(2)&gt;", hit.Snippet);
        Assert.DoesNotContain("<script", hit.Snippet, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<img", hit.Snippet, StringComparison.OrdinalIgnoreCase);
        // The only tags in the snippet are <mark> and </mark>.
        Assert.Empty(Regex.Matches(hit.Snippet, "<(?!/?mark>)"));
    }

    [Fact]
    public async Task Html_IsStrippedOnPush()
    {
        var (_, app, _) = await SeedAppAsync(fixture);
        await PushAsync(fixture, app.Id,
            Page("h", "Html page", "<main><h1>Heading</h1><script>var secretword = 1;</script><p>visible text</p></main>"));

        var doc = (await DocumentsAsync(fixture, app.Id)).Single();
        Assert.Equal("Heading visible text", doc.Body);
        Assert.Empty((await SearchAsync(fixture, app.Id, "secretword")).Items);
    }

    [Fact]
    public async Task Suggest_CompletesTitlePrefixes()
    {
        var (_, app, _) = await SeedAppAsync(fixture);
        await PushAsync(fixture, app.Id,
            Page("1", "Getting started", "intro"),
            Page("2", "Getting help", "support"),
            Page("3", "Gettysburg address", "history"),
            Page("4", "Budget planning", "getting body only"),
            Page("5", "Getting started", "duplicate title"));
        await using var db = fixture.CreateContext();
        var service = new SearchQueryService(db);

        var titles = (await service.SuggestAsync(app.Id, "gett")).Suggestions.Select(s => s.Title).ToList();
        Assert.Equal(["Getting help", "Getting started", "Gettysburg address"], titles.Order());

        Assert.Equal(["Getting started"],
            (await service.SuggestAsync(app.Id, "getting sta")).Suggestions.Select(s => s.Title));
        Assert.Empty((await service.SuggestAsync(app.Id, "zzz")).Suggestions);
    }

    [Fact]
    public async Task Suggest_ReturnsAtMostEight()
    {
        var (_, app, _) = await SeedAppAsync(fixture);
        await PushAsync(fixture, app.Id, Enumerable.Range(1, 12).Select(i => Page($"p{i}", $"Chapter {i}")).ToArray());
        await using var db = fixture.CreateContext();
        Assert.Equal(8, (await new SearchQueryService(db).SuggestAsync(app.Id, "chap")).Suggestions.Count);
    }

    [Fact]
    public async Task Queries_AreScopedToTheirTenant()
    {
        var (_, appA, _) = await SeedAppAsync(fixture);
        var (_, appB, _) = await SeedAppAsync(fixture);
        await PushAsync(fixture, appA.Id, Page("shared-id", "Zebra handbook A", "striped zebra facts"));
        await PushAsync(fixture, appB.Id, Page("shared-id", "Zebra handbook B", "striped zebra facts"),
            Page("other", "Zebra extra B", "zebra"));

        var a = await SearchAsync(fixture, appA.Id, "zebra");
        Assert.Equal(["Zebra handbook A"], a.Items.Select(i => i.Title));
        Assert.Equal(1, a.Total);
        Assert.Equal(2, (await SearchAsync(fixture, appB.Id, "zebra")).Total);

        await using var db = fixture.CreateContext();
        var suggestions = await new SearchQueryService(db).SuggestAsync(appA.Id, "zeb");
        Assert.Equal(["Zebra handbook A"], suggestions.Suggestions.Select(s => s.Title));
    }

    [Fact]
    public async Task PerDocumentLanguage_UsesItsOwnStemming()
    {
        var (_, app, _) = await SeedAppAsync(fixture);
        await PushAsync(fixture, app.Id,
            Page("fr", "Les chevaux", "Les chevaux mangent", language: "french"),
            Page("en", "Horses", "The horses are running"));

        Assert.Equal(["Les chevaux"], await TitlesAsync(fixture, app.Id, "cheval"));
        Assert.Equal(["Horses"], await TitlesAsync(fixture, app.Id, "run"));
    }

    [Fact]
    public async Task Hits_CarryMetadataSummaryAndUrl()
    {
        var (_, app, _) = await SeedAppAsync(fixture);
        await PushAsync(fixture, app.Id, Page("m", "Metadata page", "body text", url: "/docs/meta",
            summary: "A short summary", metadata: new { image = "/img.png", rank = 3 }));

        var hit = (await SearchAsync(fixture, app.Id, "metadata")).Items.Single();
        Assert.Equal("/docs/meta", hit.Url);
        Assert.Equal("A short summary", hit.Summary);
        Assert.Equal("/img.png", hit.Metadata.GetProperty("image").GetString());
        Assert.Equal("page", hit.Source);
    }

    [Fact]
    public async Task QueryLog_RecordsNormalisedQueryAndCount_OnFirstPageOnly()
    {
        var (_, app, _) = await SeedAppAsync(fixture);
        await PushAsync(fixture, app.Id, Page("l", "Logging", "log me"));

        await SearchAsync(fixture, app.Id, "  LOGGING  ", log: true);
        await SearchAsync(fixture, app.Id, "logging", page: 2, log: true);
        await SearchAsync(fixture, app.Id, "nothing-here", log: true);
        await SearchAsync(fixture, app.Id, "not logged", log: false);

        await using var db = fixture.CreateContext();
        var logs = await db.SearchQueryLogs.Where(l => l.AppId == app.Id).OrderBy(l => l.Query).ToListAsync();
        Assert.Equal([("logging", 1), ("nothing-here", 0)], logs.Select(l => (l.Query, l.ResultCount)));
    }
}

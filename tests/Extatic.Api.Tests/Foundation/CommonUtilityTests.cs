using System.Net;
using Extatic.Api.Common;
using Extatic.Api.Services;
using Extatic.Api.Tests.TestSupport;

namespace Extatic.Api.Tests.Foundation;

public class CommonUtilityTests
{
    [Fact]
    public void Csv_QuotesPerRfc4180()
    {
        var csv = new CsvBuilder()
            .AddRow("name", "note")
            .AddRow("Smith, Jane", "said \"hi\"\nthen left")
            .AddRow(null, 42)
            .ToString();

        Assert.Equal("name,note\r\n\"Smith, Jane\",\"said \"\"hi\"\"\nthen left\"\r\n,42\r\n", csv);
    }

    [Theory]
    [InlineData("=SUM(A1:A2)", "'=SUM(A1:A2)")]
    [InlineData("+1", "'+1")]
    [InlineData("-2", "'-2")]
    [InlineData("@cmd", "'@cmd")]
    [InlineData("=HYPERLINK(\"x\",\"y\")", "\"'=HYPERLINK(\"\"x\"\",\"\"y\"\")\"")]
    [InlineData("safe", "safe")]
    public void Csv_NeutralisesFormulaInjection(string input, string expected)
        => Assert.Equal(expected, CsvBuilder.Escape(input));

    [Fact]
    public void Csv_ToBytes_HasUtf8Bom()
        => Assert.Equal([0xEF, 0xBB, 0xBF], new CsvBuilder().AddRow("a").ToBytes()[..3]);

    [Theory]
    [InlineData("my-form-1", true)]
    [InlineData("a", true)]
    [InlineData("", false)]
    [InlineData("Upper", false)]
    [InlineData("under_score", false)]
    [InlineData("spa ce", false)]
    public void Slugs_Validate(string slug, bool valid)
    {
        Assert.Equal(valid, Slugs.IsValid(slug));
        if (!valid)
            Assert.Equal("form_slug", Assert.Throws<ValidationException>(() => Slugs.EnsureValid(slug, "form_slug")).Errors.Single().Pointer);
    }

    [Fact]
    public void Slugs_RejectsOver100Chars() => Assert.False(Slugs.IsValid(new string('a', 101)));

    [Theory]
    [InlineData(0, 20)]
    [InlineData(1, 0)]
    [InlineData(1, 101)]
    [InlineData(int.MaxValue, 100)]
    public void Paging_RejectsInvalid(int page, int pageSize)
        => Assert.Throws<BadRequestException>(() => Paging.Validate(page, pageSize));

    [Fact]
    public void Paging_AcceptsValid() => Paging.Validate(3, 100);

    [Fact]
    public void Tokens_AreUrlSafeAndHashStable()
    {
        var token = Tokens.NewUrlToken();
        Assert.Equal(43, token.Length);
        Assert.DoesNotContain(token, c => c is '+' or '/' or '=');
        Assert.Equal("2cf24dba5fb0a30e26e83b2ac5b9e29e1b161e5c1fa7425e73043362938b9824", Tokens.Hash("hello"));
    }

    [Fact]
    public void ClientIps_HashIsSaltedStableAndNeverRawIp()
    {
        var app = new Domain.Entities.App { Id = Guid.NewGuid() };
        var context = TestContexts.Client(app, remoteIp: IPAddress.Parse("198.51.100.7")).HttpContext;

        var hash = ClientIps.Hash(context);

        Assert.Equal(ClientIps.Hash(IPAddress.Parse("198.51.100.7"), TestServices.JwtKey), hash);
        Assert.NotEqual(ClientIps.Hash(IPAddress.Parse("198.51.100.7"), "other-salt"), hash);
        Assert.DoesNotContain("198.51.100.7", hash);
        Assert.Equal(ClientIps.Hash(IPAddress.Parse("::ffff:198.51.100.7"), TestServices.JwtKey), hash);
    }

    [Fact]
    public void SecretProtector_RoundTrips()
    {
        var protector = TestServices.SecretProtector();
        var cipher = protector.Protect("sk_test_123");
        Assert.NotEqual("sk_test_123", cipher);
        Assert.Equal("sk_test_123", protector.Unprotect(cipher));
    }
}

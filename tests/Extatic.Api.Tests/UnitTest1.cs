using Extatic.Api.Auth;

namespace Extatic.Api.Tests;

public class ApiTestFixtureTests
{
    [Fact]
    public void PlatformPrincipal_ContainsUserIdClaim()
    {
        var userId = Guid.NewGuid();
        var principal = ApiTestFixture.PlatformPrincipal(userId);

        Assert.Equal(userId.ToString(), principal.FindFirst(AppClaimTypes.UserId)?.Value);
    }
}

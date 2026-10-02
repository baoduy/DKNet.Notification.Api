using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using DKNet.Notification.Api.Configs.Handlers;
using DKNet.Notification.AppServices.Share;
using DKNet.Notification.Share;
using Moq;

namespace DKNet.Notification.App.Tests.Unit.Configs;

/// <summary>
/// DRK-1574: <see cref="IPrincipalProvider.Email"/>/<see cref="IPrincipalProvider.UserName"/> are declared
/// non-nullable, so <see cref="PrincipalProvider"/> must never hand back <c>null</c> on any path — a missing
/// <see cref="HttpContext"/>, an anonymous caller, or an authenticated caller with no matching claim.
/// </summary>
public class PrincipalProviderTests
{
    #region Methods

    private static PrincipalProvider CreateProvider(HttpContext? context)
    {
        var accessor = new Mock<IHttpContextAccessor>();
        accessor.SetupGet(a => a.HttpContext).Returns(context);
        return new PrincipalProvider(accessor.Object);
    }

    private static HttpContext CreateAuthenticatedContext(IEnumerable<Claim> claims)
    {
        var identity = new ClaimsIdentity(claims, "TestScheme");
        return new DefaultHttpContext { User = new ClaimsPrincipal(identity) };
    }

    private static HttpContext CreateAnonymousContext()
    {
        var identity = new ClaimsIdentity();
        return new DefaultHttpContext { User = new ClaimsPrincipal(identity) };
    }

    [Fact]
    public void Email_NullHttpContext_IsEmpty()
    {
        var provider = CreateProvider(null);

        provider.Email.ShouldBe(string.Empty);
    }

    [Fact]
    public void UserName_NullHttpContext_IsEmpty()
    {
        var provider = CreateProvider(null);

        provider.UserName.ShouldBe(string.Empty);
    }

    [Fact]
    public void Email_AnonymousCaller_IsEmpty()
    {
        var provider = CreateProvider(CreateAnonymousContext());

        provider.Email.ShouldBe(string.Empty);
    }

    [Fact]
    public void UserName_AnonymousCaller_IsEmpty()
    {
        var provider = CreateProvider(CreateAnonymousContext());

        provider.UserName.ShouldBe(string.Empty);
    }

    [Fact]
    public void OwnershipKey_AnonymousCaller_IsSystemAccount()
    {
        var provider = CreateProvider(CreateAnonymousContext());

        provider.GetOwnershipKey().ShouldBe(SharedConsts.SystemAccount);
    }

    [Fact]
    public void Email_AuthenticatedWithoutEmailClaim_IsEmpty()
    {
        var context = CreateAuthenticatedContext([new Claim(ClaimTypes.NameIdentifier, "client-credentials-sub")]);
        var provider = CreateProvider(context);

        provider.Email.ShouldBe(string.Empty);
    }

    [Fact]
    public void UserName_AuthenticatedWithoutNameOrEmailClaim_IsEmpty()
    {
        var context = CreateAuthenticatedContext([new Claim(ClaimTypes.NameIdentifier, "client-credentials-sub")]);
        var provider = CreateProvider(context);

        provider.UserName.ShouldBe(string.Empty);
    }

    [Fact]
    public void UserName_AuthenticatedWithEmailClaimButNoNameClaim_FallsBackToEmail()
    {
        var context = CreateAuthenticatedContext([new Claim("email", "user@example.com")]);
        var provider = CreateProvider(context);

        provider.UserName.ShouldBe("user@example.com");
    }

    [Theory]
    [InlineData("email")]
    [InlineData("emails")]
    public void EmailAndUserName_AuthenticatedWithBothClaims_ReturnClaimValues(string emailClaimType)
    {
        var context = CreateAuthenticatedContext(
        [
            new Claim(ClaimTypes.Name, "Jane Doe"),
            new Claim(emailClaimType, "jane.doe@example.com")
        ]);
        var provider = CreateProvider(context);

        provider.UserName.ShouldBe("Jane Doe");
        provider.Email.ShouldBe("jane.doe@example.com");
    }

    #endregion
}

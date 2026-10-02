using DKNet.EfCore.Extensions.Serialization;

namespace DKNet.Notification.Api.Configs.Handlers;

/// <summary>
/// Supplies the <see cref="ClaimsPrincipal"/> a role-aware <c>JsonSerializerOptions</c> is judged against —
/// the DKNet doc's recipe verbatim, over the already-registered <see cref="IHttpContextAccessor"/>.
/// </summary>
internal sealed class HttpContextSensitiveDataPrincipalAccessor(IHttpContextAccessor httpContextAccessor)
    : ISensitiveDataPrincipalAccessor
{
    public ClaimsPrincipal? Current => httpContextAccessor.HttpContext?.User;
}

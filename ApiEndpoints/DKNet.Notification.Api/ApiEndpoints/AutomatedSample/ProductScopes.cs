namespace DKNet.Notification.Api.ApiEndpoints.AutomatedSample;

/// <summary>
/// The JWT scope values a caller needs for the automated-sample Product routes. <c>AuthConfig</c>
/// registers one authorization policy per entry (the scope value doubles as its own policy name); every
/// generated CRUD route and both hand-written routes below it require one of these via
/// <c>RequireAuthorization</c>. The supplier-reference route keeps <see cref="Supplier"/> rather than
/// sharing <see cref="Write"/> — holding only <see cref="Write"/> must not be enough to assign it.
/// Stays <see langword="internal"/> (Architecture's <c>ApiTests.AllApiClassesShouldBeInternal</c> forbids
/// a public class here) — visible to <c>DKNet.Notification.App.TestSupport</c> only via
/// <c>InternalsVisibleTo</c> in this project's csproj, so its auth-handler test doubles can build their
/// default scope claim from <see cref="All"/> instead of duplicating it by hand.
/// </summary>
internal static class ProductScopes
{
    #region Fields

    public const string Read = "products.read";

    public const string Write = "products.write";

    public const string Supplier = "products.supplier";

    public const string Discontinue = "products.discontinue";

    public static readonly string[] All = [Read, Write, Supplier, Discontinue];

    #endregion
}

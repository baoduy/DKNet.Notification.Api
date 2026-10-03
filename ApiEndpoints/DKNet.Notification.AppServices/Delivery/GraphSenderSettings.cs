namespace DKNet.Notification.AppServices.Delivery;

/// <summary>The <c>Notifications:Email:Graph</c> settings. No settings file holds <see cref="ClientSecret" />.</summary>
/// <remarks>
///     Every setting is text, even the GUIDs and the credential mode: binding a value outside its type throws, and a
///     bad value must leave email "not configured" while the service still starts.
/// </remarks>
public sealed class GraphSenderSettings
{
    #region Fields

    /// <summary>The credential mode that proves the app with the Kubernetes service account token.</summary>
    public const string WorkloadIdentityCredential = "WorkloadIdentity";

    /// <summary>The credential mode that proves the app with <see cref="ClientSecret" />.</summary>
    public const string ClientSecretCredential = "ClientSecret";

    /// <summary>The credential modes the sender takes.</summary>
    public static readonly IReadOnlyList<string> CredentialModes = [WorkloadIdentityCredential, ClientSecretCredential];

    #endregion

    #region Properties

    /// <summary>Gets or sets the directory (tenant) id: required, a GUID. A tenant domain name is a bad value.</summary>
    public string TenantId { get; set; } = string.Empty;

    /// <summary>Gets or sets the application (client) id of the mail-sender app: required, a GUID.</summary>
    public string ClientId { get; set; } = string.Empty;

    /// <summary>Gets or sets the credential mode: <c>WorkloadIdentity</c> or <c>ClientSecret</c>, matched without case.</summary>
    public string Credential { get; set; } = WorkloadIdentityCredential;

    /// <summary>
    ///     Gets or sets the client secret: required with <c>ClientSecret</c>, at most 512 characters, ignored with
    ///     <c>WorkloadIdentity</c>. Secret: an environment variable, Azure App Configuration or user secrets only;
    ///     never logged.
    /// </summary>
    public string ClientSecret { get; set; } = string.Empty;

    /// <summary>Gets or sets the one sending mailbox: required, <c>local@domain</c>, at most 254 characters.</summary>
    public string Mailbox { get; set; } = string.Empty;

    #endregion
}

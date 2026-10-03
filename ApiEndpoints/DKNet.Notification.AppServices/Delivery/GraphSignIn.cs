using Azure.Core;
using Azure.Core.Pipeline;
using Azure.Identity;

namespace DKNet.Notification.AppServices.Delivery;

/// <summary>The Graph sender's sign-in to Microsoft Entra ID as the mail-sender app (ADR-0010).</summary>
public static class GraphSignIn
{
    #region Methods

    /// <summary>
    ///     The one credential of the Graph sender: workload identity or the client secret, as
    ///     <see cref="GraphSenderSettings.Credential" /> says, for the settings' tenant and app at
    ///     <see cref="GraphEndpoints.AuthorityHost" />. It never retries by itself and keeps its token until close to
    ///     expiry.
    /// </summary>
    /// <param name="settings">Good Graph settings.</param>
    /// <param name="endpoints">Where to sign in.</param>
    /// <param name="transport">The handler every token request goes through.</param>
    public static TokenCredential Credential(
        GraphSenderSettings settings,
        GraphEndpoints endpoints,
        HttpMessageHandler transport)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(endpoints);

        // The settings rule takes the braced and the N forms of a GUID too: the sign-in uses its D form.
        var tenantId = Guid.Parse(settings.TenantId).ToString("D");
        var clientId = Guid.Parse(settings.ClientId).ToString("D");
        // Microsoft's authority is validated; a test host's own authority has no instance discovery to answer it.
        var testAuthority = endpoints.AuthorityHost != GraphEndpoints.Global.AuthorityHost;
        if (string.Equals(settings.Credential, GraphSenderSettings.ClientSecretCredential, StringComparison.OrdinalIgnoreCase))
        {
            return new ClientSecretCredential(
                tenantId,
                clientId,
                settings.ClientSecret,
                Configure(new ClientSecretCredentialOptions { DisableInstanceDiscovery = testAuthority }, endpoints, transport));
        }

        // No token file in the release: the credential reads the one the cluster names in AZURE_FEDERATED_TOKEN_FILE.
        return new WorkloadIdentityCredential(Configure(
            new WorkloadIdentityCredentialOptions
            {
                TenantId = tenantId,
                ClientId = clientId,
                TokenFilePath = endpoints.ServiceAccountTokenFile,
                DisableInstanceDiscovery = testAuthority
            },
            endpoints,
            transport));
    }

    /// <summary>
    ///     The authority host, the transport, no retry (each attempt makes at most one token request, R1), no request
    ///     log and no trace (R2). The library's own Azure-Identity entries are dropped by the log set-up instead.
    /// </summary>
    private static T Configure<T>(T options, GraphEndpoints endpoints, HttpMessageHandler transport)
        where T : TokenCredentialOptions
    {
        options.AuthorityHost = endpoints.AuthorityHost;
        options.Transport = new HttpClientTransport(transport);
        options.Retry.MaxRetries = 0;
        options.Diagnostics.IsLoggingEnabled = false;
        options.Diagnostics.IsDistributedTracingEnabled = false;
        return options;
    }

    #endregion
}

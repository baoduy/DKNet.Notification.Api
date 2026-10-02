namespace DKNet.Notification.Api.Configs.Handlers;

internal sealed class PrincipalProvider(IHttpContextAccessor accessor) : IPrincipalProvider
{
    #region Fields

    private string _email = string.Empty;
    private bool _initialized;
    private string? _ownershipKey;
    private string _userName = string.Empty;

    #endregion

    #region Properties

    public Guid ProfileId
    {
        get
        {
            Initialize();
            return Guid.TryParse(_ownershipKey, out var id) ? id : Guid.Empty;
        }
    }

    public string Email
    {
        get
        {
            Initialize();
            return _email;
        }
    }

    public string UserName
    {
        get
        {
            Initialize();
            return _userName;
        }
    }

    #endregion

    #region Methods

    public string? GetOwnershipKey()
    {
        Initialize();
        return _ownershipKey;
    }

    // DRK-1466: deliberately identical to GetOwnershipKey() today — OwnedBy and CreatedBy/UpdatedBy
    // share one claim for now. Do not collapse into GetOwnershipKey(): ServiceConfigs.AddCurrentUserProvider
    // wires this to the audit hook independently of AddDataOwnerProvider, so the two are free to diverge later.
    public string? GetCurrentUser()
    {
        Initialize();
        return _ownershipKey;
    }

    private void Initialize()
    {
        var context = accessor.HttpContext;
        if (context == null)
        {
            return;
        }

        if (_initialized)
        {
            return;
        }

        if (context.User.Identity?.IsAuthenticated != true)
        {
            _ownershipKey = SharedConsts.SystemAccount;
            _initialized = true;
            return;
        }

        _userName = context.User.Identity.Name ?? string.Empty;

        //Get ownership key from subject claims, first non-empty wins
        string[] subjectClaimTypes =
        [
            "http://schemas.microsoft.com/identity/claims/objectidentifier", "oid", ClaimTypes.NameIdentifier, "sub"
        ];
        foreach (var claimType in subjectClaimTypes)
        {
            var claim = context.User.FindFirst(c => string.Equals(c.Type, claimType, StringComparison.OrdinalIgnoreCase));
            if (claim != null && !string.IsNullOrWhiteSpace(claim.Value))
            {
                _ownershipKey = claim.Value;
                break;
            }
        }

        //Get email
        var email = context.User.FindFirst(c =>
            c.Type.Equals("emails", StringComparison.OrdinalIgnoreCase) ||
            c.Type.Equals("email", StringComparison.OrdinalIgnoreCase));
        if (email != null)
        {
            _email = email.Value;
            if (string.IsNullOrEmpty(_userName))
            {
                _userName = _email;
            }
        }

        _initialized = true;
    }

    #endregion
}

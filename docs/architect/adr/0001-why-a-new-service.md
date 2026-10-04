# ADR-0001: A new service for notification delivery

- **Status:** Accepted
- **Context:**
  - Several backend services will need to send email and Teams messages. DKNet.Accounts.Api is a likely first one.
  - No DKNet package sends email or posts to Teams today.
  - Delivery needs its own secrets: an SMTP password and Teams webhook URLs.
  - Templates change on their own rhythm, separate from any caller's releases.
  - The requester asked for a separate repo, DKNet.Notification.Api, and created it.
- **Decision:** Build DKNet Notification as its own deployable service, in its own repo, scaffolded from DKNet.Templates.
- **Alternatives:**
  - *Grow DKNet.Accounts.Api.* Rejected: notifications are not ledger work. Every other caller would then depend on the ledger service, and the ledger would hold mail and Teams secrets.
  - *A DKNet library package that callers embed.* Rejected: every caller would hold channel secrets and templates, and a template change would mean releasing every caller.
  - *Each caller talks to SMTP and Teams itself.* Rejected: it duplicates rendering, encoding and retry logic, which is the problem this service removes.
- **Consequences:**
  - Easier: one place for templates, secrets and channel rules; a new channel is one change here.
  - Harder: one more service to deploy and run; callers take an HTTP dependency and must handle 503.

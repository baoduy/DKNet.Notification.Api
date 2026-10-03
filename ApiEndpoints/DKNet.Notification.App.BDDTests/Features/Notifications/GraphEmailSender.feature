Feature: Microsoft Graph email sender

  # DRK-2028 §5 (spec revision 16). Gherkin copied from the spec.
  # Surface A (brief DRK-2033 §7): the @integration scenarios of the Graph settings, the sender choice at start-up,
  # the sender change and the health check, steps in Steps/GraphEmailSenderSteps.cs. Its @unit scenarios live in
  # DKNet.Notification.App.Tests/Unit: RetryAfterWaitTests ("A wait above 60 seconds is cut to 60 seconds") and
  # ReleasedSettingsFilesTests ("No released settings file holds a Graph client secret"). "The local run still sends
  # to the mail catcher" lives in DKNet.Notification.App.Tests/Scaffold/LocalAppHostTests, the one project allowed
  # Aspire.Hosting.Testing.
  # Surface B (brief DRK-2031 §7): the scenarios that send through the Graph stub and the token stub, steps in
  # Steps/GraphDeliverySteps.cs. Its @unit scenario "The Graph sender uses Microsoft's global cloud" lives in
  # DKNet.Notification.App.Tests/Unit/Delivery/GraphGlobalCloudTests.
  # Every expected value in the step definitions is a literal from the spec; the setting names are the contract
  # names of brief DRK-2033 §5.

  Rule: An email is sent through Graph from the one mailbox when the sender is Graph

    Background:
      Given the service runs with its released template catalogue, sign-in on and email set up to send through the Graph stub from the mailbox "notify@contoso.com"
      And "treasury-ops" is a caller allowed to send notifications

    @integration
    Scenario: An email reaches Graph from the one mailbox
      When "treasury-ops" emails template "account-opened" to "jane@example.com" for customer "Jane Tan" and account "0012345678"
      Then the call is accepted with a new notification id
      And the Graph stub records 1 send from "notify@contoso.com" to "jane@example.com" only, with the subject "Your account is open"
      And the send has an HTML body that says "Dear Jane Tan, your account 0012345678 is open."
      And the send has no copy recipient, no attachment, no sender address, no sender name and no Sent Items choice

    @integration
    Scenario: A caller cannot choose the mailbox
      When "treasury-ops" emails template "account-opened" to "jane@example.com" with an extra parameter "from" set to "ceo@contoso.com"
      Then the Graph stub records 1 send from "notify@contoso.com" to "jane@example.com" only
      And no send names "ceo@contoso.com"

    @integration
    Scenario Outline: Any 2xx answer from Graph ends Delivered
      Given Graph answers the send with <answer>
      When "treasury-ops" emails template "account-opened" to "jane@example.com"
      Then 1 delivered entry on attempt 1 is logged for the notification id

      Examples:
        | answer   |
        | HTTP 202 |
        | HTTP 200 |

    @integration
    Scenario: A Graph delivery is logged and counted
      When "treasury-ops" emails template "account-opened" to "jane@example.com" for customer "Jane Tan" and account "0012345678"
      Then 1 queued entry and 1 delivered entry on attempt 1 are logged for the notification id
      And both entries carry the trace id of the call
      And the accepted count for "email" with outcome "queued" and the delivered count for "email" each rose by 1
      And 1 delivery duration is recorded for "email"

    @integration
    Scenario: No personal data, token or secret reaches the logs, traces or kept records
      Given the Graph sender signs in with the client secret "Gr4ph-s3cret-9921" at the token stub, which issues the token "tok-5521-abc"
      When "treasury-ops" emails template "account-opened" to "jane@example.com" for customer "Jane Tan" and account "0012345678"
      Then the Graph stub records 1 send to "jane@example.com"
      And no log entry, trace or kept idempotency record holds "jane@example.com", "Jane Tan", "0012345678", "Gr4ph-s3cret-9921" or "tok-5521-abc"

    @integration
    Scenario Outline: A provider's error text never reaches the logs
      Given <step> answers attempt 1 with <code> and the error text "<text>"
      When "treasury-ops" emails template "account-opened" to "jane@example.com"
      Then the attempt failure entry holds the status code <code>
      And no log entry holds "<secret part>"

      Examples:
        | step           | code | text                               | secret part      |
        | Graph          | 400  | Invalid recipient jane@example.com | jane@example.com |
        | the token stub | 400  | Bad request, trace 7f3a-9921       | 7f3a-9921        |

  Rule: Only the chosen sender is active

    @integration
    Scenario Outline: Only the chosen sender gets the mail
      Given the service runs with sign-in on, email on with <sender choice>, and both the mail catcher and the Graph stub ready
      And "treasury-ops" is a caller allowed to send notifications
      When "treasury-ops" emails template "account-opened" to "jane@example.com"
      Then <receiver> receives 1 mail to "jane@example.com"
      And <idle> receives nothing

      Examples:
        | sender choice      | receiver         | idle                              |
        | the sender "Graph" | the Graph stub   | the mail catcher                  |
        | the sender "graph" | the Graph stub   | the mail catcher                  |
        | the sender "Smtp"  | the mail catcher | the Graph stub and the token stub |
        | no sender given    | the mail catcher | the Graph stub and the token stub |

    @integration
    Scenario Outline: The other sender's settings are not read
      Given the service started with email on, the sender "<sender>", every setting of that sender given, and <other fault>
      And "treasury-ops" is a caller allowed to send notifications
      When "treasury-ops" emails template "account-opened" to "jane@example.com"
      Then 1 start-up entry names the email sender "<sender>"
      And <receiver> receives 1 mail to "jane@example.com"

      Examples:
        | sender | other fault                             | receiver         |
        | Graph  | no mail server host                     | the Graph stub   |
        | Smtp   | the tenant id "contoso.onmicrosoft.com" | the mail catcher |

    @integration
    Scenario: A sender change takes effect only at the next start
      Given the service started with email on and the sender "Smtp", with the mail catcher and the Graph stub ready
      And "treasury-ops" is a caller allowed to send notifications
      And the sender is changed to "Graph" while the service runs
      When "treasury-ops" emails template "account-opened" to "jane@example.com"
      Then the mail catcher receives 1 mail to "jane@example.com"
      And the Graph stub receives nothing

  Rule: The service signs in as the mail-sender app

    Background:
      Given the service runs with its released template catalogue, sign-in on and email set up to send through the Graph stub, with the delivery waits of 5 seconds and 30 seconds
      And the mail-sender app "7c1d4e2a-0b9f-4a63-8e15-2d6f9b3c8a41" signs in with a client secret at the token stub
      And "treasury-ops" is a caller allowed to send notifications

    @integration
    Scenario: 2 notifications in a row ask for 1 token
      When "treasury-ops" emails "jane@example.com" and then "john@example.com"
      Then the Graph stub records 2 sends, each with the token the token stub issued
      And the token stub received 1 token request, from the app "7c1d4e2a-0b9f-4a63-8e15-2d6f9b3c8a41", for Microsoft Graph

    @integration
    Scenario Outline: Each sign-in answer is retried or not by its kind
      Given the token stub answers the first token request with <answer>
      When "treasury-ops" emails template "account-opened" to "jane@example.com"
      Then attempt 1 is logged as a "<kind>" failure with <code>
      And <outcome>

      Examples:
        | answer                                     | kind      | code               | outcome                                                                 |
        | HTTP 503                                   | transient | the status code 503 | the mail is sent on attempt 2, after 2 token requests in all            |
        | HTTP 408                                   | transient | the status code 408 | the mail is sent on attempt 2, after 2 token requests in all            |
        | HTTP 429 with a retry after of 2 seconds   | transient | the status code 429 | the mail is sent on attempt 2, no sooner than 2 seconds and sooner than 5 seconds after attempt 1 |
        | no answer within the attempt's time limit  | transient | no status code     | the mail is sent on attempt 2                                           |
        | a refused connection                       | transient | no status code     | the mail is sent on attempt 2                                           |
        | a lost connection                          | transient | no status code     | the mail is sent on attempt 2                                           |
        | HTTP 401 with "invalid_client"             | permanent | the status code 401 | the notification fails with no attempt 2, after 1 token request         |
        | HTTP 400 with "invalid_request"            | permanent | the status code 400 | the notification fails with no attempt 2, after 1 token request         |

  Rule: Workload identity signs in with the service account token, and fails at once without it

    @integration
    Scenario: Workload identity signs in with the service account token
      Given the service runs with sign-in on and email set up to send through the Graph stub by workload identity, signing in at the token stub with the service account token "sa-token-4417"
      And the Graph settings also hold the client secret "Gr4ph-s3cret-9921"
      And "treasury-ops" is a caller allowed to send notifications
      When "treasury-ops" emails template "account-opened" to "jane@example.com"
      Then the token stub received 1 token request that carries "sa-token-4417" and not "Gr4ph-s3cret-9921"
      And the Graph stub records 1 send to "jane@example.com", and no log entry holds "sa-token-4417"

    @integration
    Scenario: Workload identity with no service account token fails at once
      Given the service runs with sign-in on and email set up to send through the Graph stub by workload identity, with no service account token given to it
      And "treasury-ops" is a caller allowed to send notifications
      When "treasury-ops" emails template "account-opened" to "jane@example.com"
      Then attempt 1 is logged as a "permanent" failure with no status code
      And the notification fails with no attempt 2, and the Graph stub receives nothing

  Rule: Graph answers are retried by their kind, at most 3 attempts in all

    Background:
      Given the service runs with its released template catalogue, sign-in on and email set up to send through the Graph stub, with the delivery waits of 5 seconds and 30 seconds
      And "treasury-ops" is a caller allowed to send notifications

    @integration
    Scenario Outline: Each Graph answer is retried or not by its kind
      Given Graph answers attempt 1 with <answer>
      When "treasury-ops" emails template "account-opened" to "jane@example.com"
      Then attempt 1 is logged as a "<kind>" failure with <code>
      And <outcome>

      Examples:
        | answer                                                                 | kind      | code                | outcome                                                                                     |
        | HTTP 503                                                               | transient | the status code 503 | the mail is sent on attempt 2, no sooner than 5 seconds after attempt 1                     |
        | HTTP 408                                                               | transient | the status code 408 | the mail is sent on attempt 2                                                               |
        | a refused connection                                                   | transient | no status code      | the mail is sent on attempt 2                                                               |
        | a lost connection                                                      | transient | no status code      | the mail is sent on attempt 2                                                               |
        | a send it records but answers only after the attempt's time limit      | transient | no status code      | the mail is sent on attempt 2, and the Graph stub holds 2 sends to "jane@example.com"       |
        | a slow token and a slow send that together pass the attempt's time limit | transient | no status code      | the mail is sent on attempt 2                                                               |
        | HTTP 400                                                               | permanent | the status code 400 | the notification fails with no attempt 2                                                    |
        | HTTP 401                                                               | permanent | the status code 401 | the notification fails with no attempt 2                                                    |
        | HTTP 403                                                               | permanent | the status code 403 | the notification fails with no attempt 2                                                    |
        | HTTP 404                                                               | permanent | the status code 404 | the notification fails with no attempt 2                                                    |
        | HTTP 302 to "https://elsewhere.example"                                | permanent | the status code 302 | the notification fails with no attempt 2, and "https://elsewhere.example" receives nothing |

    @integration
    Scenario: A notification fails after 3 transient Graph answers
      Given Graph answers 503 to every send
      When "treasury-ops" emails template "account-opened" to "jane@example.com"
      Then 3 attempts are made, with 3 sends in all
      And 3 attempt failures and 1 failure error with attempt count 3 are logged
      And the failed count for "email" rose by 1

  Rule: After a 429 the next attempt waits the time Graph asks, at most 60 seconds

    Background:
      Given the service runs with its released template catalogue, sign-in on and email set up to send through the Graph stub, with the delivery waits of 5 seconds and 30 seconds
      And "treasury-ops" is a caller allowed to send notifications

    @integration
    Scenario Outline: The wait after a 429 follows the retry-after header
      Given Graph answers attempt 1 with 429 and <header>
      When "treasury-ops" emails template "account-opened" to "jane@example.com"
      Then the mail is sent on attempt 2, <timing>

      Examples:
        | header                                | timing                                                             |
        | a retry after of 2 seconds            | no sooner than 2 seconds and sooner than 5 seconds after attempt 1 |
        | a retry after of 0 seconds            | sooner than 5 seconds after attempt 1                              |
        | a retry-after date 3 seconds from now | no sooner than 2 seconds and sooner than 5 seconds after attempt 1 |
        | a retry-after date in the past        | sooner than 5 seconds after attempt 1                              |
        | no retry-after header                 | no sooner than 5 seconds after attempt 1                           |
        | the retry-after value "soon"          | no sooner than 5 seconds after attempt 1                           |

    @integration
    Scenario: A notification waiting after a 429 does not hold up the next one
      Given Graph answers the first send for "jane@example.com" with 429 and a retry after of 10 seconds
      When "treasury-ops" emails "jane@example.com" and then "john@example.com"
      Then the mail to "john@example.com" is sent before the mail to "jane@example.com"
      And the mail to "jane@example.com" is sent on attempt 2

  Rule: The service checks its Graph settings when it starts

    @integration
    Scenario Outline: A set-up Graph sender is logged at start-up
      Given email is on, with the sender "Graph", every Graph setting given and <credential>
      When the service starts
      Then 1 start-up entry names the email sender "Graph"
      And no warning says email is not set up

      Examples:
        | credential                                                  |
        | the credential mode "WorkloadIdentity" and no client secret |
        | the credential mode "workloadidentity" and no client secret |
        | the credential mode "WorkloadIdentity" and a client secret of 513 characters |
        | the credential mode "ClientSecret" and a client secret      |

    @integration
    Scenario Outline: Email on but not set up for Graph still lets the service start
      Given the service started with email on and <fault>
      And "treasury-ops" is a caller allowed to send notifications
      When "treasury-ops" emails template "account-opened" to "jane@example.com"
      Then the call is accepted and skipped with reason "ChannelNotConfigured", and the Graph stub receives nothing
      And the start-up wrote 1 warning that email is not set up, naming <setting> and no setting value

      Examples:
        | fault                                                                   | setting             |
        | the sender "Graph" and no mailbox                                       | the mailbox         |
        | the sender "Graph" and the mailbox "notify.contoso.com"                 | the mailbox         |
        | the sender "Graph" and a mailbox of 255 characters                      | the mailbox         |
        | the sender "Graph" and the mailbox "notify@contoso.com, ops@contoso.com" | the mailbox        |
        | the sender "Graph" and no tenant id                                     | the tenant id       |
        | the sender "Graph" and the tenant id "contoso.onmicrosoft.com"          | the tenant id       |
        | the sender "Graph" and the client id "mail-sender"                      | the client id       |
        | the sender "Graph" and the credential mode "Certificate"                | the credential mode |
        | the sender "Graph", the credential mode "ClientSecret" and no client secret | the client secret |
        | the sender "Graph", the credential mode "ClientSecret" and a client secret of 513 characters | the client secret |
        | the sender "SendGrid"                                                   | the sender choice   |

  Rule: A Graph outage does not touch the health check

    @integration
    Scenario: A Graph outage does not touch the health check
      Given the service runs with sign-in on and email set up to send through the Graph stub
      And the Graph stub and the token stub are stopped
      When the cluster's liveness probe asks the health check without a token
      Then the answer is 200 with the status "Healthy"

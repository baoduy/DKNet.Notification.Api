Feature: Microsoft Graph email sender

  # DRK-2028 §5 (spec revision 16). Gherkin copied from the spec.
  # Surface A (brief DRK-2033 §7): the @integration scenarios of the Graph settings, the sender choice at start-up,
  # the sender change and the health check, steps in Steps/GraphEmailSenderSteps.cs. Its @unit scenarios live in
  # DKNet.Notification.App.Tests/Unit: RetryAfterWaitTests ("A wait above 60 seconds is cut to 60 seconds") and
  # ReleasedSettingsFilesTests ("No released settings file holds a Graph client secret"). "The local run still sends
  # to the mail catcher" lives in DKNet.Notification.App.Tests/Scaffold/LocalAppHostTests, the one project allowed
  # Aspire.Hosting.Testing.
  # Surface B (brief DRK-2031) adds the scenarios that send through the Graph stub and the token stub.
  # Every expected value in the step definitions is a literal from the spec; the setting names are the contract
  # names of brief DRK-2033 §5.

  Rule: Only the chosen sender is active

    @integration
    Scenario: A sender change takes effect only at the next start
      Given the service started with email on and the sender "Smtp", with the mail catcher and the Graph stub ready
      And "treasury-ops" is a caller allowed to send notifications
      And the sender is changed to "Graph" while the service runs
      When "treasury-ops" emails template "account-opened" to "jane@example.com"
      Then the mail catcher receives 1 mail to "jane@example.com"
      And the Graph stub receives nothing

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

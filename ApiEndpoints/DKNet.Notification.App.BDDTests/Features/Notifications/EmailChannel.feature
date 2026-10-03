Feature: Email channel with the SMTP sender, rendering and delivery

  # DRK-2020 §5 (spec revision 19). Gherkin copied from the spec.
  # Surface A (brief DRK-2025 §7): the @integration scenarios of the email settings, the recipient check, the
  # rendering refusal and the delivery queue. Its @unit scenarios live in DKNet.Notification.App.Tests/Unit/
  # Notifications: EmailRendererTests (rule "The template is filled from the parameters"), DeliverySettingsTests
  # ("A bad delivery setting stops the start-up", one row also through the host in Integration/Notifications) and
  # ReleasedSettingsFilesTests ("No released settings file holds an SMTP password").
  # Surface B (brief DRK-2023 §7): the delivery, retry, repeated-call, connection, health check and local-run
  # scenarios, steps in Steps/EmailDeliverySteps.cs. "The local run starts the mail catcher" lives in
  # DKNet.Notification.App.Tests/Scaffold/LocalAppHostTests, the one project allowed Aspire.Hosting.Testing.
  # Every expected value in the step definitions is a literal from the spec; the setting names are the leader's
  # contract names (brief DRK-2025 §5).

  Rule: A valid email call is rendered, queued and delivered

    Background:
      Given the service runs with its released template catalogue, sign-in on and email set up to send to the mail catcher
      And "treasury-ops" is a caller allowed to send notifications

    @integration
    Scenario: An email reaches the recipient
      When "treasury-ops" emails template "account-opened" to "jane@example.com" for customer "Jane Tan" and account "0012345678"
      Then the call is accepted with a new notification id
      And the mail catcher holds 1 mail to "jane@example.com" with the subject "Your account is open"
      And the mail says "Dear Jane Tan, your account 0012345678 is open."
      And the mail comes from the sender address and sender name of the settings
      And the mail has no other recipient, no attachment and an HTML body only

    @integration
    Scenario: A delivery is logged and counted
      When "treasury-ops" emails template "account-opened" to "jane@example.com" for customer "Jane Tan" and account "0012345678"
      Then 1 queued entry and 1 delivered entry on attempt 1 are logged for the notification id
      And both entries carry the trace id of the call
      And the accepted count for "email" with outcome "queued" and the delivered count for "email" each rose by 1
      And 1 delivery duration is recorded for "email"

    @integration
    Scenario: No personal data or secret reaches the logs
      Given the mail catcher asks the service to sign in with the password "Pa55-w0rd-7781"
      When "treasury-ops" emails template "account-opened" to "jane@example.com" for customer "Jane Tan" and account "0012345678"
      Then the mail catcher holds 1 mail to "jane@example.com"
      And no log entry, trace or kept idempotency record holds "jane@example.com", "Jane Tan", "0012345678" or "Pa55-w0rd-7781"

    @integration
    Scenario: A value cannot add markup to the mail
      When "treasury-ops" emails template "account-opened" to "jane@example.com" for customer "<b>Jane</b>" and account "0012345678"
      Then the mail shows "<b>Jane</b>" as text, not in bold

    @integration
    Scenario: A value cannot add a mail header
      Given the template "account-alert" has the email subject "Alert for {{customerName}}"
      When "treasury-ops" emails template "account-alert" to "jane@example.com" for customer "Jane", a line feed and "Bcc: eve@example.com"
      Then the mail's subject is "Alert for Jane Bcc: eve@example.com"
      And the mail has only the recipient "jane@example.com"

  Rule: The template is filled from the parameters

    # The @unit scenarios of this rule live in DKNet.Notification.App.Tests/Unit/Notifications/EmailRendererTests.

    @integration
    Scenario: Of 2 names that differ only in case, the first in the call fills the token
      Given the service runs with sign-in on and email set up to send to the mail catcher
      And "treasury-ops" is a caller allowed to send notifications
      When "treasury-ops" emails template "account-opened" to "jane@example.com" with "customerName" set to "Jane", then "CustomerName" set to "John", and account "0012345678"
      Then the mail says "Dear Jane, your account 0012345678 is open."

  Rule: An email call that cannot be delivered is skipped before its recipient is checked

    @integration
    Scenario Outline: An email call is skipped when email is not ready for it
      Given the service runs with sign-in on and <set-up>
      And "treasury-ops" is a caller allowed to send notifications
      When "treasury-ops" emails template "<template>" with no recipient
      Then the call is accepted with a new notification id
      And the skip warning names reason "<reason>", and the accepted count for "email" with outcome "skipped" rose by 1
      And the mail catcher receives no mail

      Examples:
        | set-up                                                                                  | template       | reason               |
        | email off                                                                               | account-opened | ChannelNotConfigured |
        | email on with the sender "SendGrid"                                                     | account-opened | ChannelNotConfigured |
        | email set up to the mail catcher, and a template "team-digest" with only a Teams version | team-digest    | NoTemplateVersion    |

    @integration
    Scenario Outline: Every other channel is still skipped
      Given the service runs with sign-in on and email set up to send to the mail catcher
      And "treasury-ops" is a caller allowed to send notifications
      When "treasury-ops" sends template "account-opened" to "jane@example.com" on channel "<channel>"
      Then the call is accepted with a new notification id
      And the skip warning names reason "ChannelNotSupported"
      And the mail catcher receives no mail

      Examples:
        | channel  |
        | whatsapp |

  Rule: The recipient and every token are checked before the call is queued

    Background:
      Given the service runs with its released template catalogue, sign-in on and email set up to send to the mail catcher
      And "treasury-ops" is a caller allowed to send notifications

    @integration
    Scenario Outline: A missing or bad recipient is refused
      When "treasury-ops" emails template "account-opened" to <recipient> for customer "Jane Tan" and account "0012345678"
      Then the call is refused with "<code>" for the recipient, and a trace id
      And 1 rejection entry is logged with "<code>", and the rejected count for "<code>" rose by 1
      And the mail catcher receives no mail

      Examples:
        | recipient                     | code              |
        | no address                    | RECIPIENT_MISSING |
        | an empty address              | RECIPIENT_MISSING |
        | "Jane <jane@example.com>"     | RECIPIENT_INVALID |
        | "a@example.com,b@example.com" | RECIPIENT_INVALID |
        | "jane.example.com"            | RECIPIENT_INVALID |
        | " jane@example.com"           | RECIPIENT_INVALID |
        | an address of 255 characters  | RECIPIENT_INVALID |

    @integration
    Scenario: A missing parameter is refused
      When "treasury-ops" emails template "account-opened" to "jane@example.com" for customer "Jane Tan" with no account
      Then the call is refused with "PARAMETER_MISSING" naming "accountNumber"
      And 1 rejection entry is logged with "PARAMETER_MISSING", and the rejected count for "PARAMETER_MISSING" rose by 1
      And the mail catcher receives no mail

  Rule: A full queue refuses new calls

    @integration
    Scenario: A full queue answers 503
      Given the service runs with sign-in on, email set up to send to the mail catcher and a queue size of 2
      And "treasury-ops" is a caller allowed to send notifications
      And 2 notifications of "treasury-ops" are not delivered yet, because the mail catcher is stopped
      When "treasury-ops" emails template "account-opened" to "jane@example.com"
      Then the call is refused with "QUEUE_FULL", status 503 and a retry after 30 seconds
      And 1 rejection entry is logged with "QUEUE_FULL", the rejected count for "QUEUE_FULL" rose by 1, and the queue length shows 2

  Rule: A transient failure is retried, at most 3 attempts in all

    Background:
      Given the service runs with its released template catalogue, sign-in on and email set up to send to the mail catcher
      And "treasury-ops" is a caller allowed to send notifications

    @integration
    Scenario Outline: Each failure is retried or not by its kind
      Given attempt 1 meets <failure>
      When "treasury-ops" emails template "account-opened" to "jane@example.com"
      Then attempt 1 is logged as a "<kind>" failure
      And <outcome>

      Examples:
        | failure                                       | kind      | outcome                                        |
        | the SMTP reply 451                            | transient | the mail is delivered on attempt 2             |
        | a mail server that does not answer in time    | transient | the mail is delivered on attempt 2             |
        | a refused connection                          | transient | the mail is delivered on attempt 2             |
        | the SMTP reply 550                            | permanent | the notification fails with no attempt 2       |
        | the SMTP reply 535 for the sign-in            | permanent | the notification fails with no attempt 2       |

    @integration
    Scenario: A mail is delivered when the mail server comes back
      Given the mail catcher is stopped
      And "treasury-ops" emailed template "account-opened" to "jane@example.com"
      When the mail catcher starts again before attempt 2
      Then the mail catcher holds 1 mail to "jane@example.com"
      And 1 transient attempt failure and 1 delivery on attempt 2 are logged

    @integration
    Scenario: A notification fails after 3 transient failures
      Given the mail catcher is stopped
      When "treasury-ops" emails template "account-opened" to "jane@example.com"
      Then 3 attempts are made, attempt 2 no sooner than 5 seconds after attempt 1, and attempt 3 no sooner than 30 seconds after attempt 2
      And 3 attempt failures and 1 failure error with attempt count 3 are logged
      And the failed count for "email" rose by 1

    @integration
    Scenario: A waiting notification does not hold up the next one
      Given the mail server answers 451 to the first attempt for "jane@example.com" only
      When "treasury-ops" emails "jane@example.com" and then "john@example.com"
      Then the mail to "john@example.com" is delivered before the mail to "jane@example.com"
      And the mail to "jane@example.com" is delivered on attempt 2

    @integration
    Scenario: A stop loses a notification that has not ended
      Given the mail catcher is stopped
      And "treasury-ops" emailed template "account-opened" to "jane@example.com", and it waits for attempt 2
      When the service is restarted after the mail catcher is back
      Then the mail catcher receives no mail to "jane@example.com"

    @integration
    Scenario: An SMTP reply text never reaches the logs
      Given the mail server answers attempt 1 with "550 5.1.1 <jane@example.com> unknown user"
      When "treasury-ops" emails template "account-opened" to "jane@example.com"
      Then the attempt failure entry holds the reply code 550
      And no log entry holds "jane@example.com"

  Rule: A repeated call never sends a second mail

    Background:
      Given the service runs with its released template catalogue, sign-in on and email set up to send to the mail catcher
      And "treasury-ops" is a caller allowed to send notifications

    @integration
    Scenario: A repeated call sends 1 mail
      Given "treasury-ops" emailed template "account-opened" to "jane@example.com" with key "k-5001"
      When "treasury-ops" sends the same call again with key "k-5001"
      Then the second answer carries the same notification id as the first
      And the mail catcher holds exactly 1 mail to "jane@example.com"
      And exactly 1 queued entry is logged, and the queued and delivered counts for "email" each rose by 1 only

    @integration
    Scenario: A new token for the same caller still sends 1 mail
      Given "treasury-ops" emailed template "account-opened" to "jane@example.com" with key "k-5002" on its first token
      When "treasury-ops" sends the same call with key "k-5002" on a new token
      Then the second answer carries the same notification id as the first
      And the mail catcher holds exactly 1 mail to "jane@example.com"

    @integration
    Scenario: 2 callers with the same key send 2 mails
      Given "card-ops" is also a caller allowed to send notifications
      And "treasury-ops" emailed template "account-opened" to "jane@example.com" with key "k-5003"
      When "card-ops" emails template "account-opened" to "jane@example.com" with key "k-5003"
      Then "card-ops" gets a different notification id from "treasury-ops"
      And the mail catcher holds 2 mails to "jane@example.com"

  Rule: Mail leaves only over an encrypted connection with a checked certificate, signed in as the settings say

    @integration
    Scenario Outline: Mail goes over the encryption the settings choose
      Given the service runs with sign-in on and email set up to send to a mail server that <server>
      And "treasury-ops" is a caller allowed to send notifications
      When "treasury-ops" emails template "account-opened" to "jane@example.com"
      Then the mail server holds 1 mail to "jane@example.com", received over <encryption>

      Examples:
        | server                                                    | encryption |
        | offers STARTTLS, with the security mode STARTTLS set     | STARTTLS   |
        | speaks TLS from the first byte, with the security mode TLS set | TLS        |

    @integration
    Scenario Outline: The service signs in only when the settings name a user
      Given the service runs with sign-in on and email set up to send to a mail catcher that <catcher>
      And the SMTP settings hold <credentials>
      And "treasury-ops" is a caller allowed to send notifications
      When "treasury-ops" emails template "account-opened" to "jane@example.com"
      Then the mail catcher holds 1 mail to "jane@example.com"

      Examples:
        | catcher                                                   | credentials                                         |
        | offers no sign-in                                         | no user name                                        |
        | accepts only the user "notify-svc" with "Pa55-w0rd-7781"  | the user "notify-svc" and the password "Pa55-w0rd-7781" |

    @integration
    Scenario: A mail server with a certificate the service does not trust gets no mail
      Given the service runs with sign-in on and email set up to send to a mail server whose certificate the service does not trust
      And "treasury-ops" is a caller allowed to send notifications
      When "treasury-ops" emails template "account-opened" to "jane@example.com"
      Then the mail server receives no mail
      And the notification fails after 3 transient attempt failures

  Rule: An SMTP outage does not touch the health check

    @integration
    Scenario: An SMTP outage does not touch the health check
      Given the service runs with sign-in on and email set up to send to the mail catcher
      And the mail catcher is stopped
      When the cluster's liveness probe asks the health check without a token
      Then the answer is 200 with the status "Healthy"

  Rule: The service checks its email settings when it starts

    @integration
    Scenario: A set-up email sender is logged at start-up
      Given email is on, with the sender "Smtp" and every SMTP setting given
      When the service starts
      Then 1 start-up entry names the email sender "Smtp"
      And no warning says email is not set up

    @integration
    Scenario: Email off logs no email start-up entry
      Given email is off
      When the service starts
      Then no email start-up entry is logged

    @integration
    Scenario Outline: Email on but not set up still lets the service start
      Given the service started with email on and <fault>
      And "treasury-ops" is a caller allowed to send notifications
      When "treasury-ops" emails template "account-opened" to "jane@example.com"
      Then the call is accepted and skipped with reason "ChannelNotConfigured"
      And the start-up wrote 1 warning that email is not set up, naming <setting> and no setting value

      Examples:
        | fault                                    | setting                 |
        | no mail server host                      | the mail server host    |
        | no sender address                        | the sender address      |
        | the sender address "notify.example.com"  | the sender address      |
        | the port 70000                           | the port                |
        | the security mode "None"                 | the security mode       |
        | a time limit of 500 seconds              | the time limit          |
        | the sender "SendGrid"                    | the sender choice       |

    @integration
    Scenario: A settings change takes effect only at the next start
      Given the service started with email off
      And "treasury-ops" is a caller allowed to send notifications
      And the email settings are turned on while the service runs
      When "treasury-ops" emails template "account-opened" to "jane@example.com"
      Then the call is accepted and skipped with reason "ChannelNotConfigured"

  Rule: The local run shows each mail

    @integration
    Scenario: A local email shows up in the mail catcher
      Given the service runs with the local-run settings: sign-in off and email set up to send to the mail catcher over STARTTLS
      When Minh, a developer, emails template "account-opened" to "jane@example.com" for customer "<b>Jane</b>" and account "0012345678"
      Then the call is accepted with a new notification id
      And within 10 seconds Minh can read 1 mail to "jane@example.com" in the mail catcher, with the subject "Your account is open"
      And the mail shows "<b>Jane</b>" as text, not in bold

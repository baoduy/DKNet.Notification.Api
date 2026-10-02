Feature: Email channel with the SMTP sender, rendering and delivery

  # DRK-2020 §5 (spec revision 19), surface A (brief DRK-2025 §7): the @integration scenarios of the email
  # settings, the recipient check, the rendering refusal and the delivery queue. Gherkin copied from the spec.
  # The @unit scenarios of surface A live in DKNet.Notification.App.Tests/Unit/Notifications: EmailRendererTests
  # (rule "The template is filled from the parameters"), DeliverySettingsTests ("A bad delivery setting stops the
  # start-up", one row also through the host in Integration/Notifications) and ReleasedSettingsFilesTests ("No
  # released settings file holds an SMTP password"). Every other scenario of §5 needs the delivery worker and the
  # SMTP sender, so surface B adds it here.
  # Every expected value in the step definitions is a literal from the spec; the setting names are the leader's
  # contract names (brief §5).

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
        | email on with the sender "Graph"                                                        | account-opened | ChannelNotConfigured |
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
        | teams    |
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
        | the sender "Graph"                       | the sender choice       |
        | the sender "SendGrid"                    | the sender choice       |

    @integration
    Scenario: A settings change takes effect only at the next start
      Given the service started with email off
      And "treasury-ops" is a caller allowed to send notifications
      And the email settings are turned on while the service runs
      When "treasury-ops" emails template "account-opened" to "jane@example.com"
      Then the call is accepted and skipped with reason "ChannelNotConfigured"

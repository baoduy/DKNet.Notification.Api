Feature: Microsoft Teams channel

  # DRK-2035 §5 (spec revision 13). Gherkin copied from the spec, every scenario and every Examples row, kept whole
  # (the gate allowed, not required, a split of the 3-rule scenarios).
  # Surface A (brief DRK-2036 §7): the checks of a teams call, the skip reasons, the size check and the Teams settings
  # read at start-up. Its @unit scenarios "The base settings keep Teams off" and "No released settings file holds a
  # webhook URL" live in DKNet.Notification.App.Tests/Unit/Notifications/ReleasedSettingsFilesTests.
  # Surface B (brief DRK-2037 §7): the post, the card, the retry kinds, the time limit, the certificate, the shared
  # queue and the leak checks.
  # Steps in Steps/TeamsChannelSteps.cs (Given, When, checks and skips) and Steps/TeamsDeliverySteps.cs (the post,
  # the card, attempts and logs), with the shared state in Steps/TeamsScenario.cs. Every expected value is a literal
  # from the spec; the setting names are the contract names of brief DRK-2036 §5 (Notifications:Teams:*). The webhook
  # stub is a RecordingHttpStub; its path holds neither "ops-alerts" nor the signature, which sits in the query.

  Rule: A valid Teams call posts one card to the named destination

    Background:
      Given the service runs with sign-in on and Teams on, with the destination "ops-alerts" pointing at the webhook stub
      And the template "staff-account-opened" has a Teams version with the title "Account {{accountNumber}} opened" and the body "**{{customerName}}** opened account {{accountNumber}}."
      And "treasury-ops" is a caller allowed to send notifications

    @integration
    Scenario: A card reaches the Teams destination
      When "treasury-ops" posts template "staff-account-opened" to the Teams destination "ops-alerts" for customer "Jane Tan" and account "0012345678"
      Then the call is accepted with a new notification id
      And the webhook stub records 1 post: a message with 1 Adaptive Card
      And the card holds the title "Account 0012345678 opened" and the Markdown text "**Jane Tan** opened account 0012345678."
      And both text blocks wrap, and the mail catcher receives nothing

    @integration
    Scenario: A Teams delivery is logged and counted
      When "treasury-ops" posts template "staff-account-opened" to the Teams destination "ops-alerts" for customer "Jane Tan" and account "0012345678"
      Then 1 queued entry and 1 delivered entry on attempt 1 are logged for the notification id
      And both entries carry the trace id of the call
      And the accepted count for "teams" with outcome "queued" and the delivered count for "teams" each rose by 1
      And 1 delivery duration is recorded for "teams"

    @integration
    Scenario Outline: Any 2xx answer from the webhook ends Delivered
      Given the webhook answers the post with <answer>
      When "treasury-ops" posts template "staff-account-opened" to the Teams destination "ops-alerts"
      Then 1 delivered entry on attempt 1 is logged for the notification id

      Examples:
        | answer   |
        | HTTP 202 |
        | HTTP 200 |

    @integration
    Scenario: The channel name is matched without case
      When "treasury-ops" posts template "staff-account-opened" on channel "Teams" to the Teams destination "ops-alerts"
      Then the webhook stub records 1 post
      And the queued entry names the channel "teams"

    @integration
    Scenario: A value stays text and cannot change the card
      When "treasury-ops" posts template "staff-account-opened" to the Teams destination "ops-alerts" for customer "\"}]},{\"type\":\"Image\"" and account "0012345678"
      Then the webhook stub records 1 post with exactly 1 Adaptive Card of 2 text blocks
      And the Markdown text holds the customer value exactly as sent

    @integration
    Scenario: Markdown in a value reaches the card unchanged
      When "treasury-ops" posts template "staff-account-opened" to the Teams destination "ops-alerts" for customer "[Jane](https://example.com)" and account "0012345678"
      Then the card's Markdown text is "**[Jane](https://example.com)** opened account 0012345678."

    @integration
    Scenario: Teams delivers while email is off
      Given email is off
      When "treasury-ops" posts template "staff-account-opened" to the Teams destination "ops-alerts"
      Then the webhook stub records 1 post

  Rule: The card has a title block only when there is a title

    @integration
    Scenario Outline: The title block follows the filled title
      Given the service runs with sign-in on and Teams on, with the destination "ops-alerts" pointing at the webhook stub
      And the template "staff-note" has a Teams version with <title> and the body "Note for {{team}}."
      And "treasury-ops" is a caller allowed to send notifications
      When "treasury-ops" posts template "staff-note" to the Teams destination "ops-alerts" for team "Treasury" <extra>
      Then the posted card holds <title block> and the Markdown text "Note for Treasury."

      Examples:
        | title                          | extra                                 | title block                         |
        | no title                       | with no other value                   | no title block                      |
        | the title "{{headline}}"       | with headline ""                      | no title block                      |
        | the title "{{headline}}"       | with a headline of 500 characters     | the title of all 500 characters     |
        | the title "{{headline}}"       | with a headline of 501 characters     | the first 500 characters as title   |

  Rule: A Teams call is checked before it is queued

    Background:
      Given the service runs with sign-in on and Teams on, with the destination "ops-alerts" pointing at the webhook stub
      And the template "staff-account-opened" has a Teams version
      And "treasury-ops" is a caller allowed to send notifications

    @integration
    Scenario Outline: A bad destination name is refused
      When "treasury-ops" posts template "staff-account-opened" <destination>
      Then the call is refused with "<code>" naming the Teams destination
      And nothing is queued, and the webhook stub receives nothing

      Examples:
        | destination                                     | code              |
        | with no Teams destination                       | RECIPIENT_MISSING |
        | to the Teams destination ""                     | RECIPIENT_MISSING |
        | to the Teams destination "Ops-Alerts"           | RECIPIENT_INVALID |
        | to the Teams destination "ops alerts"           | RECIPIENT_INVALID |
        | to the Teams destination " ops-alerts"          | RECIPIENT_INVALID |
        | to the Teams destination "ops_alerts"           | RECIPIENT_INVALID |
        | to a Teams destination name of 65 characters    | RECIPIENT_INVALID |

    @integration
    Scenario: A destination that is not set is skipped
      When "treasury-ops" posts template "staff-account-opened" to the Teams destination "finance"
      Then the call is accepted with a new notification id
      And 1 skip warning is logged with reason "TeamsDestinationNotConfigured", and it does not hold "finance"
      And the webhook stub receives nothing

    @integration
    Scenario: A missing parameter is refused
      When "treasury-ops" posts template "staff-account-opened" to the Teams destination "ops-alerts" for customer "Jane Tan" with no account
      Then the call is refused with "PARAMETER_MISSING" naming the parameter "accountNumber"
      And nothing is queued

    @integration
    Scenario Outline: A posted message above 28,672 bytes is refused
      Given the template "staff-digest" has a Teams version whose body holds the 8 tokens "{{part1}}" to "{{part8}}"
      When "treasury-ops" posts template "staff-digest" to the Teams destination "ops-alerts" with plain-text values that make the posted message <size> bytes
      Then <outcome>

      Examples:
        | size   | outcome                                                                                   |
        | 28,672 | the call is accepted, and the webhook stub records 1 post of 28,672 bytes                 |
        | 28,673 | the call is refused with "MESSAGE_TOO_LARGE" naming no field, and nothing is queued        |

  Rule: A Teams call is skipped when Teams or the template cannot serve it

    @integration
    Scenario Outline: The skip reason names why
      Given the service runs with sign-in on and <set-up>
      And "treasury-ops" is a caller allowed to send notifications
      When "treasury-ops" sends template "<template>" on channel "<channel>" to the Teams destination "ops-alerts"
      Then the call is accepted with a new notification id
      And the skip warning names channel "<logged>" and reason "<reason>", and the webhook stub receives nothing

      Examples:
        | set-up                                                         | template             | channel  | logged   | reason                        |
        | the released settings                                          | account-opened       | teams    | teams    | ChannelNotConfigured          |
        | Teams on, with "ops-alerts" set, and a Teams template          | account-opened       | teams    | teams    | NoTemplateVersion             |
        | Teams on with no destination, and a Teams template             | staff-account-opened | teams    | teams    | TeamsDestinationNotConfigured |
        | Teams on, with "ops-alerts" set, and a Teams template          | staff-account-opened | whatsapp | whatsapp | ChannelNotSupported           |

  Rule: Webhook answers are retried by their kind, at most 3 attempts in all

    Background:
      Given the service runs with sign-in on and Teams on, with the destination "ops-alerts" pointing at the webhook stub, and the delivery waits of 5 seconds and 30 seconds
      And the template "staff-account-opened" has a Teams version
      And "treasury-ops" is a caller allowed to send notifications

    @integration
    Scenario Outline: Each webhook answer is retried or not by its kind
      Given the webhook answers attempt 1 with <answer>
      When "treasury-ops" posts template "staff-account-opened" to the Teams destination "ops-alerts"
      Then attempt 1 is logged as a "<kind>" failure with <code>
      And <outcome>

      Examples:
        | answer                                                              | kind      | code                | outcome                                                                                       |
        | HTTP 503                                                            | transient | the status code 503 | the card is posted on attempt 2, no sooner than 5 seconds after attempt 1                     |
        | HTTP 408                                                            | transient | the status code 408 | the card is posted on attempt 2                                                               |
        | HTTP 429 with a retry after of 2 seconds                            | transient | the status code 429 | the card is posted on attempt 2, no sooner than 2 seconds and sooner than 5 seconds after attempt 1 |
        | HTTP 429 with no retry-after header                                 | transient | the status code 429 | the card is posted on attempt 2, no sooner than 5 seconds after attempt 1                     |
        | a refused connection                                                | transient | no status code      | the card is posted on attempt 2                                                               |
        | a lost connection                                                   | transient | no status code      | the card is posted on attempt 2                                                               |
        | a post it records but answers only after the Teams time limit       | transient | no status code      | the card is posted on attempt 2, and the webhook stub holds 2 posts                           |
        | HTTP 400                                                            | permanent | the status code 400 | the notification fails with no attempt 2                                                      |
        | HTTP 404                                                            | permanent | the status code 404 | the notification fails with no attempt 2                                                      |
        | HTTP 302 to "https://elsewhere.example"                             | permanent | the status code 302 | the notification fails with no attempt 2, and "https://elsewhere.example" receives nothing   |

    @integration
    Scenario: A notification fails after 3 transient webhook answers
      Given the webhook answers 503 to every post
      When "treasury-ops" posts template "staff-account-opened" to the Teams destination "ops-alerts"
      Then 3 attempts are made, with 3 posts in all
      And 3 attempt failures and 1 failure error with attempt count 3 are logged
      And the failed count for "teams" rose by 1

    @integration
    Scenario: The Teams time limit is Teams' own
      Given the Teams time limit is 2 seconds and the email time limit is 30 seconds
      And the webhook answers attempt 1 only after 3 seconds
      When "treasury-ops" posts template "staff-account-opened" to the Teams destination "ops-alerts"
      Then attempt 1 is logged as a "transient" failure with no status code
      And the card is posted on attempt 2

    @integration
    Scenario: A webhook whose certificate fails the check gets no card
      Given the destination "ops-alerts" answers with a certificate the service does not trust
      When "treasury-ops" posts template "staff-account-opened" to the Teams destination "ops-alerts"
      Then attempt 1 is logged as a "transient" failure with no status code
      And the webhook stub records no post

    @integration
    Scenario: Email and Teams share the places of the queue
      Given the delivery queue has 1 place, and email is set up to send to the mail catcher
      And a Teams notification waits for its attempt 2 after a 429 with a retry after of 30 seconds
      When "treasury-ops" emails template "account-opened" to "jane@example.com"
      Then the call is refused with "QUEUE_FULL"
      And the mail catcher receives nothing

    @integration
    Scenario: A Teams notification waiting after a 429 does not hold up the next one
      Given email is set up to send to the mail catcher
      And the webhook answers the first post with 429 and a retry after of 10 seconds
      When "treasury-ops" posts a card to "ops-alerts" for customer "Jane Tan" and then emails "john@example.com"
      Then the mail to "john@example.com" is sent before the card for "Jane Tan" is posted
      And the card for "Jane Tan" is posted on attempt 2

  Rule: The service checks its Teams settings when it starts, and still starts

    @integration
    Scenario Outline: A bad destination counts as not set, and the others still work
      Given the service started with sign-in on, Teams on, the destination "ops-alerts" pointing at the webhook stub, and the destination "finance" with <fault>
      And the template "staff-account-opened" has a Teams version
      And "treasury-ops" is a caller allowed to send notifications
      When "treasury-ops" posts template "staff-account-opened" to "finance" and then to "ops-alerts"
      Then the call to "finance" is skipped with reason "TeamsDestinationNotConfigured"
      And the webhook stub records 1 post, for "ops-alerts"

      Examples:
        | fault                                                     |
        | no webhook URL                                            |
        | an empty webhook URL                                      |
        | the webhook URL "http://127.0.0.1/hook"                   |
        | the webhook URL "hooks/finance"                           |
        | a webhook URL of 2,049 characters                         |

    @integration
    Scenario: A destination name that breaks the rule is not set
      Given the service started with sign-in on, Teams on, and the destination "Ops-Alerts" pointing at the webhook stub
      And the template "staff-account-opened" has a Teams version
      And "treasury-ops" is a caller allowed to send notifications
      When "treasury-ops" posts template "staff-account-opened" to the Teams destination "ops-alerts"
      Then the call is skipped with reason "TeamsDestinationNotConfigured"
      And the webhook stub receives nothing

    @integration
    Scenario Outline: A bad Teams setting leaves Teams not configured, and email still works
      Given the service started with sign-in on, email set up to send to the mail catcher, Teams settings with the destination "ops-alerts", and <fault>
      And "treasury-ops" is a caller allowed to send notifications
      When "treasury-ops" posts a card to "ops-alerts" and emails "jane@example.com"
      Then the Teams call is skipped with reason "ChannelNotConfigured", and the webhook stub receives nothing
      And the mail catcher receives 1 mail to "jane@example.com", and no start-up entry is about Teams

      Examples:
        | fault                              |
        | Teams on and a time limit of 0 seconds    |
        | Teams on and a time limit of 121 seconds  |
        | the Teams on/off value "yes"              |
        | Teams on and 101 destinations             |

    @integration
    Scenario: A Teams settings change takes effect only at the next start
      Given the service started with Teams on and no destination
      And "treasury-ops" is a caller allowed to send notifications
      And the destination "ops-alerts" is added while the service runs
      When "treasury-ops" posts a card to "ops-alerts"
      Then the call is skipped with reason "TeamsDestinationNotConfigured"
      And the webhook stub receives nothing

  Rule: No secret or personal data leaves through logs, traces or kept records

    Background:
      Given the service runs with sign-in on and tracing on, and Teams on with the destination "ops-alerts" pointing at the webhook stub with the signature "Wb-s1gn-7731"
      And the template "staff-account-opened" has a Teams version
      And "treasury-ops" is a caller allowed to send notifications

    @integration
    Scenario: No value, destination name or signature reaches the logs, traces or kept records
      When "treasury-ops" posts template "staff-account-opened" to the Teams destination "ops-alerts" for customer "Jane Tan" and account "0012345678"
      Then the webhook stub records 1 post
      And no log entry, metric, trace or kept idempotency record holds "Jane Tan", "0012345678", "ops-alerts" or "Wb-s1gn-7731"

    @integration
    Scenario: The webhook's answer text never reaches the logs
      Given the webhook answers attempt 1 with 400 and the text "Bad card for Jane Tan"
      When "treasury-ops" posts template "staff-account-opened" to the Teams destination "ops-alerts" for customer "Jane Tan"
      Then the attempt failure entry holds the status code 400
      And no log entry holds "Jane Tan"

  Rule: A Teams outage does not touch the health check

    @integration
    Scenario: A Teams outage does not touch the health check
      Given the service runs with sign-in on and Teams on, with the destination "ops-alerts" pointing at the webhook stub
      And the webhook stub is stopped
      When the cluster's liveness probe asks the health check without a token
      Then the answer is 200 with the status "Healthy"

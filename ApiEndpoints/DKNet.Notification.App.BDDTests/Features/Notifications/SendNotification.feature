Feature: Send API: the send endpoint, sign-in, idempotency and skip

  # DRK-2013 §5, feature "Send API, template catalogue and skip rule": the scenarios surface B (DRK-2016) delivers.
  # Surface A's scenarios live in Features/Startup/StartupChecks.feature and DKNet.Notification.App.Tests.
  # Gherkin copied from the spec, with the two changes the spec gate (DRK-2012) asked for:
  #   - "A call refused for its body holds no key" is an outline, with a 413 row next to the INVALID_REQUEST row;
  #   - the caller-id outline keeps its actor name "treasury-ops" (the gate allowed, not required, a rename).
  # Every expected value in the step definitions is a literal from the spec.
  # DRK-2020 §3 Step 5 (brief DRK-2025 row 14): this host keeps email off, so an email call is now skipped with
  # "ChannelNotConfigured"; every other channel keeps "ChannelNotSupported".

  Rule: A valid call is accepted, logged and skipped

    Background:
      Given the service runs with its released template catalogue and sign-in on
      And "treasury-ops" is a caller allowed to send notifications

    @integration
    Scenario: A valid email call is accepted and skipped
      When "treasury-ops" asks to email template "account-opened" to "jane@example.com" with key "k-1001"
      Then the call is accepted with a new notification id
      And exactly 1 skip warning is logged with reason "ChannelNotConfigured" and caller "treasury-ops"

    @integration
    Scenario Outline: Every channel is skipped in this release
      When "treasury-ops" sends template "account-opened" on channel "<sent>"
      Then the call is accepted with a new notification id
      And the skip warning names channel "<logged>" and reason "<reason>"

      Examples:
        | sent     | logged   | reason               |
        | email    | email    | ChannelNotConfigured |
        | Teams    | teams    | ChannelNotSupported  |
        | whatsapp | whatsapp | ChannelNotSupported  |

    @integration
    Scenario: An email call with an empty parameter list is still skipped in this release
      When "treasury-ops" sends template "account-opened" on channel "email" with an empty parameter list
      Then the call is accepted with a new notification id
      And the skip warning names reason "ChannelNotConfigured"

    @integration
    Scenario: No personal data reaches the logs
      When "treasury-ops" emails template "account-opened" to "jane@example.com" for customer "Jane Tan" and account "0012345678"
      Then no log entry holds "jane@example.com", "Jane Tan" or "0012345678"

  Rule: Only a signed-in caller with the permission may send

    Background:
      Given the service runs with its released template catalogue and sign-in on

    @integration
    Scenario Outline: The permission is read from any of its 3 claims
      Given "card-ops" holds a token with "notifications.send" in its "<claim>" claim
      When "card-ops" emails template "account-opened" to "jane@example.com"
      Then the call is accepted with a new notification id

      Examples:
        | claim |
        | scp   |
        | scope |
        | roles |

    @integration
    Scenario Outline: A caller without a good token is refused before anything else
      Given "card-ops" calls with <credential>
      When "card-ops" emails template "account-opened" to "jane@example.com"
      Then the call is refused with status <status> and an empty body
      And no skip warning is logged

      Examples:
        | credential                                | status |
        | no token                                  | 401    |
        | an invalid token                          | 401    |
        | a token that names no calling application | 401    |
        | a token without the permission            | 403    |

    @integration
    Scenario Outline: The caller id comes from the first caller claim
      Given "treasury-ops" holds a token with the permission, "<first>" in its "<first claim>" claim and "<second>" in its "<second claim>" claim
      When "treasury-ops" emails template "account-opened" to "jane@example.com"
      Then the skip warning names caller "<first>"

      Examples:
        | first claim | first        | second claim | second      |
        | client_id   | treasury-ops | azp          | console-app |
        | client_id   | treasury-ops | appid        | legacy-app  |
        | azp         | console-app  | appid        | legacy-app  |

  Rule: A local run without sign-in uses the caller "System"

    @integration
    Scenario: A local run without sign-in shows the skip
      Given a developer runs the service locally with its released template catalogue and sign-in off
      When the developer emails template "account-opened" to "jane@example.com" with key "k-3001"
      Then the call is accepted with a new notification id
      And the skip warning names caller "System"

  Rule: A repeated call is never processed twice

    Background:
      Given the service runs with its released template catalogue and sign-in on
      And "treasury-ops" is a caller allowed to send notifications

    @integration
    Scenario: A repeated call is answered with the first answer
      Given "treasury-ops" emailed template "account-opened" to "jane@example.com" with key "k-1002"
      When "treasury-ops" sends the same call again with key "k-1002"
      Then the second answer carries the same notification id as the first
      And exactly 1 skip warning is logged, and the accepted count rose by 1

    @integration
    Scenario: A new token for the same caller keeps the replay
      Given "treasury-ops" emailed template "account-opened" with key "k-1003" on its first token
      When "treasury-ops" sends the same call with key "k-1003" on a new token
      Then the second answer carries the same notification id as the first
      And exactly 1 skip warning is logged

    @integration
    Scenario: 2 callers can use the same key
      Given "card-ops" is also a caller allowed to send notifications
      And "treasury-ops" emailed template "account-opened" with key "k-2001"
      When "card-ops" emails template "account-opened" with key "k-2001"
      Then "card-ops" gets a different notification id from "treasury-ops"
      And 2 skip warnings are logged

    @integration
    Scenario Outline: A key is held for 30 seconds after a refused call
      Given "treasury-ops" sent template "account-closed" with key "<key>" and was refused with "TEMPLATE_NOT_FOUND"
      When "treasury-ops" sends template "account-opened" with key "<key>" <wait> later
      Then the second call <result>

      Examples:
        | key    | wait       | result                                 |
        | k-4001 | 5 seconds  | is refused with status 409             |
        | k-4002 | 31 seconds | is accepted with a new notification id |

    @integration
    Scenario Outline: A call refused for its body holds no key
      Given "treasury-ops" sent <refused call> and key "<key>" and was refused with <refusal>
      When "treasury-ops" emails template "account-opened" with key "<key>" at once
      Then the second call is accepted with a new notification id

      Examples:
        | refused call                      | key    | refusal           |
        | a call with an empty channel      | k-4004 | "INVALID_REQUEST" |
        | a call whose body is 65,537 bytes | k-4005 | status 413        |

    @integration
    Scenario: A repeat of a call that is still running is refused
      Given "treasury-ops" emailed template "account-opened" with key "k-4003"
      And the service is still working on that call
      When "treasury-ops" sends the same call again with key "k-4003"
      Then the second call is refused with status 409

    @integration
    Scenario Outline: A bad idempotency key is refused
      When "treasury-ops" emails template "account-opened" with <key>
      Then the call is refused with status 400
      And no skip warning is logged

      Examples:
        | key                     |
        | no key                  |
        | a key of 256 characters |
        | the key "k 1001"        |

  Rule: The body and the template are checked before the skip

    Background:
      Given the service runs with its released template catalogue and sign-in on
      And "treasury-ops" is a caller allowed to send notifications

    @integration
    Scenario: A body larger than 64 KB is refused
      When "treasury-ops" sends a call whose body is 65,537 bytes
      Then the call is refused with status 413

    @integration
    Scenario Outline: A body that breaks a field rule is refused
      When "treasury-ops" sends a call with <fault>
      Then the call is refused with "INVALID_REQUEST" and a trace id
      And 1 rejection entry is logged with "INVALID_REQUEST", and the rejected count for it rose by 1

      Examples:
        | fault                                        |
        | an empty channel                             |
        | a channel of 51 characters                   |
        | a template id of 101 characters              |
        | no parameter list at all                     |
        | the parameter "amount" set to the number 100 |
        | 51 parameters                                |
        | the parameter "customer-name"                |
        | a parameter value of 4,001 characters        |
        | a body that is not JSON                      |

    @integration
    Scenario Outline: An unknown template is refused
      When "treasury-ops" emails template "<template>" to "jane@example.com"
      Then the call is refused with "TEMPLATE_NOT_FOUND"
      And 1 rejection entry is logged with "TEMPLATE_NOT_FOUND" and template "<template>"

      Examples:
        | template       |
        | account-closed |
        | Account-Opened |

  Rule: The service checks its set-up when it starts, and its health check stays quiet

    @integration
    Scenario: The catalogue does not change while the service runs
      Given the service started with its released template catalogue and sign-in on
      And "treasury-ops" is a caller allowed to send notifications
      And the template settings are changed to remove "account-opened" while the service runs
      When "treasury-ops" emails template "account-opened" to "jane@example.com"
      Then the call is accepted with a new notification id

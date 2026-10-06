Feature: Notification client package

  # DRK-2141 §5 (spec revision 17). Gherkin copied from the spec; brief DRK-2145.
  # The @integration scenarios drive DKNet.Notification.Client against the real service host (SendApiFactory,
  # sign-in on, TestAuthHandler for Entra ID), its primary handler the test server's own; the @unit scenarios
  # answer from a scripted stub (Steps/ClientHarness.cs). No production seam: the test container points the
  # client's HttpClient at the host through an IHttpMessageHandlerBuilderFilter in the caller's container.
  # The service mints every notification id itself (Guid.CreateVersion7, no seam), so a quoted id a step says a
  # caller "got" or "billing-api" "sent" names the id that call really got; later steps compare against that.
  # A quoted id no step bound, such as the one "the service never issued", is sent as written.

  @integration
  Scenario: A caller sends an email notification and gets its notification id
    Given the notification service has the template "account-opened" registered for email
    And "accounts-api" uses the notification client with its own token handler
    When "accounts-api" sends "account-opened" by email to "jane@example.com" for customer "Jane" with key "onboard-0012345678"
    Then "accounts-api" gets back the notification id the service issued

  @integration
  Scenario: Sending again with the same key gives the same notification id
    Given "accounts-api" sent "account-opened" to "jane@example.com" with key "onboard-0012345678" and got notification id "8c7e0f3a-2b61-4d0e-9a3f-5d8b1c6e2f47"
    When "accounts-api" sends the same notification again with key "onboard-0012345678"
    Then "accounts-api" gets notification id "8c7e0f3a-2b61-4d0e-9a3f-5d8b1c6e2f47" again
    And the service sends only 1 email to "jane@example.com"

  @integration
  Scenario: A caller reads the status of its delivered notification
    Given "accounts-api" sent "account-opened" with key "onboard-0012345678" and got notification id "8c7e0f3a-2b61-4d0e-9a3f-5d8b1c6e2f47"
    And the email to "jane@example.com" is delivered
    When "accounts-api" reads the status of "8c7e0f3a-2b61-4d0e-9a3f-5d8b1c6e2f47"
    Then "accounts-api" sees status "success" with key "onboard-0012345678"

  @integration
  Scenario Outline: Reading an unknown or foreign notification is refused as not found
    Given "accounts-api" uses the notification client
    When "accounts-api" reads the status of <notification>
    Then the client raises its refusal with status 404 and code "NOTIFICATION_NOT_FOUND"
    And no status is returned

    Examples:
      | notification                                                              |
      | "0b1d2e3f-4a5b-4c6d-8e7f-9a0b1c2d3e4f", which the service never issued   |
      | "5d8b1c6e-2f47-4a3f-9d0e-8c7e0f3a2b61", which "billing-api" sent          |

  @integration
  Scenario Outline: A refused send reaches the caller as a code, not as JSON
    Given "accounts-api" uses the notification client
    When "accounts-api" sends <request>
    Then the client raises its refusal with status <status> and code "<code>"
    And the refusal carries the message the service sent for it
    And the refusal names <at fault> as the part at fault

    Examples:
      | request                                           | status | code               | at fault        |
      | template "no-such-template" by email              | 400    | TEMPLATE_NOT_FOUND | the template id |
      | "account-opened" while the delivery queue is full | 503    | QUEUE_FULL         | no part         |

  @integration
  Scenario: A refusal with an empty body still reaches the caller
    Given "accounts-api" holds a token without the "notifications.send" permission
    When "accounts-api" sends "account-opened" to "jane@example.com" with key "onboard-0012345679"
    Then the client raises its refusal with status 403 and an empty error list

  @integration
  Scenario: The service refuses an empty idempotency key
    Given "accounts-api" uses the notification client
    When "accounts-api" sends "account-opened" to "jane@example.com" with an empty key
    Then the client raises its refusal with status 400
    And no notification is queued

  @unit
  Scenario: The client never repeats a refused send
    Given the service answers every send from "accounts-api" with status 503 and code "QUEUE_FULL"
    When "accounts-api" sends "account-opened" to "jane@example.com" with key "onboard-0012345680"
    Then the service receives exactly 1 send
    And the client raises its refusal with status 503

  @unit
  Scenario: Registered with the service address only, the client sends no credential
    Given "accounts-api" registers the notification client with the service address only
    When "accounts-api" sends "account-opened" to "jane@example.com" with key "onboard-0012345681"
    Then the send reaches the service with no credential

  @unit
  Scenario: The caller's own handler runs on every request
    Given "accounts-api" registers the notification client with the service address and its own token handler
    When "accounts-api" sends a notification and then reads its status
    Then the token handler ran on both requests

  @integration
  Scenario: Every live caller route has exactly one client operation
    Given the notification service's live caller routes
    When the route parity check compares them with the client's operations
    Then each live caller route matches exactly 1 client operation
    And each client operation matches exactly 1 live caller route
    And the health route has no client operation

  @integration
  Scenario: The packed client package is complete
    Given the client package is packed
    When a developer opens the package
    Then it carries a README with install, register, send and read-status steps
    And it references no project of the notification service
    And its project and repository links point at the DKNet.Notification.Api repo

  @integration
  Scenario: The service's own projects never take the client package or Refit
    Given the notification service's API, application, domain and shared projects
    When their package and project references are checked
    Then none of them depends on the client package
    And none of them depends on Refit

  @unit
  Scenario: The client never logs or keeps the caller's credential
    Given "accounts-api" registers the notification client with its own token handler, which adds token "eyJ-test-token-0001"
    When "accounts-api" sends "account-opened" to "jane@example.com" with key "onboard-0012345682"
    Then no log entry written by the client holds "eyJ-test-token-0001"
    And once the token handler stops adding a token, the next send reaches the service with no credential

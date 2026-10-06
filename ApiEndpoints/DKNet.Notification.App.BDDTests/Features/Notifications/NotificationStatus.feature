Feature: Notification status

  A caller reads where its own notification stands: pending, success or failed (spec §7, §8).

  # Spec 2026-10-05 "Notification status and Redis delivery", §6–§8, §10 and §11 (Task 7). Every scenario runs on
  # this feature's own Redis container: the status records and the delivery list live there, and each scenario's
  # first Given empties it. The service, call and mail steps are the email feature's (Steps/EmailChannelSteps.cs and
  # Steps/EmailDeliverySteps.cs, scoped to this feature too); the status and restart steps are in
  # Steps/NotificationStatusSteps.cs.

  Background:
    Given "treasury-ops" is a caller allowed to send notifications

  @integration
  Scenario: A delivered email reads success
    Given the service runs with sign-in on and email set up to send to the mail catcher
    When "treasury-ops" emails template "account-opened" to "jane@example.com" with key "st-1001"
    Then the call is accepted with a new notification id
    And within 10 seconds the status of that notification reads "success" with the key "st-1001"

  @integration
  Scenario: An email that fails 3 times reads failed
    Given the service runs with sign-in on and email set up to send to the mail catcher, and the delivery waits of 1 second and 1 second
    And the mail catcher is stopped
    When "treasury-ops" emails template "account-opened" to "jane@example.com" with key "st-1002"
    Then the status of that notification reads "pending"
    And within 15 seconds the status of that notification reads "failed"
    And the notification fails after 3 transient attempt failures

  @integration
  Scenario: A skipped call reads failed at once
    Given the service runs with sign-in on and email off
    When "treasury-ops" emails template "account-opened" to "jane@example.com" with key "st-1003"
    Then the call is accepted and skipped with reason "ChannelNotConfigured"
    And the status of that notification reads "failed"

  @integration
  Scenario: Another caller cannot read the status
    Given the service runs with sign-in on and email set up to send to the mail catcher
    And "payments" is also a caller allowed to send notifications
    When "treasury-ops" emails template "account-opened" to "jane@example.com" with key "st-1004"
    Then within 10 seconds the status of that notification reads "success"
    And "payments" reading the status of that notification is answered 404 with "NOTIFICATION_NOT_FOUND"

  @integration
  Scenario: A notification waiting for its retry is delivered by the next host after a restart
    Given the service runs with sign-in on and email set up to send to the mail catcher, and the delivery waits of 3 seconds and 30 seconds
    And the mail catcher is stopped
    And "treasury-ops" emailed template "account-opened" to "jane@example.com", and it waits for attempt 2
    When the service is stopped during the wait, with the notification left in Redis
    And the mail catcher is started
    And the service is started again on the same Redis
    Then the mail catcher holds 1 mail to "jane@example.com"
    And the next host delivered it on attempt 2
    And within 5 seconds the status of that notification reads "success"

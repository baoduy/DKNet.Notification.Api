Feature: DKNet Notification starts as an empty service

  # DRK-1994 §5. The AppHost scenario ("The local run starts Redis and the API, and no database") needs
  # Aspire.Hosting.Testing, which the spec allows in DKNet.Notification.App.Tests only — it lives there as
  # Scaffold/LocalAppHostTests. The @unit scenarios live in DKNet.Notification.App.Tests/Scaffold too.

  @integration
  Scenario: The health check answers without sign-in
    Given the Notification API is running with no database
    When the cluster's liveness probe asks the health check without a token
    Then the answer is 200 with the status "Healthy"
    And the answer holds no other detail

  @integration
  Scenario Outline: The template's other health routes are gone
    Given the Notification API is running with its deployed settings
    And the caller "accounts-api" holds a valid Entra ID token
    When "accounts-api" asks for the <template route>
    Then the answer is 404

    Examples:
      | template route         |
      | root address           |
      | detailed health report |

  @integration
  Scenario: Every other request needs a token
    Given the Notification API is running with its deployed settings
    When "accounts-api" asks for the root address without a token
    Then the answer is 401

  @integration
  Scenario Outline: No sample feature remains
    Given the Notification API is running with its deployed settings
    And the caller "accounts-api" holds a valid Entra ID token
    When "accounts-api" asks for the template's <sample> list
    Then the answer is 404

    Examples:
      | sample          |
      | products        |
      | purchase orders |

  @integration
  Scenario: A local run needs no sign-in
    Given the Notification API is running in the local Development environment
    When "accounts-api" asks for the root address without a token
    Then the answer is 404

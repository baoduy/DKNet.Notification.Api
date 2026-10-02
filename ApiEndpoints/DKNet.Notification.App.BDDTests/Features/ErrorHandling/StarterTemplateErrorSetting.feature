@ErrorHandling
Feature: Starter template answers unhandled errors and command failures from the library's error setting

  Background:
    Given the service is running with no Redis connection configured

  Scenario: A service from the starter template answers an unexpected error from the library
    Given a service generated from the `DKNet.Templates` starter registers the standard error setting and runs in production
    When storefront sends a request that raises an unexpected error
    Then the response body carries the title "Error", a status, a type, a trace identifier and an error list in the standard shape

  Scenario: A starter template endpoint gets the registered setting without naming it
    Given a service generated from the `DKNet.Templates` starter registers a setting that answers 409 for a failure marked "precondition"
    When an endpoint of that service answers a command failure marked "precondition"
    Then the response is 409

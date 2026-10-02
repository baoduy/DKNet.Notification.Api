Feature: Send API start-up checks and a quiet health check

  # DRK-2013 §5, feature "Send API, template catalogue and skip rule": the @integration scenarios of the rule below
  # that surface A (DRK-2018) delivers. The @unit scenarios of the same rule, "The release ships the sample
  # template" and "A broken template catalogue stops the start-up", live in
  # DKNet.Notification.App.Tests/Unit/Templates (one host-level row in Integration/Templates). "The catalogue does
  # not change while the service runs" needs the send endpoint, so surface B writes it.

  Rule: The service checks its set-up when it starts, and its health check stays quiet

    @integration
    Scenario Outline: A deployment needs Redis
      Given the service is set to run as "<environment>" with no Redis connection
      When the service starts
      Then the service <result>

      Examples:
        | environment | result                                                  |
        | Production  | refuses to start and names the missing Redis connection |
        | Development | starts and keeps idempotency records in memory          |

    @integration
    Scenario: A health probe writes no log entry
      Given the service runs with its released template catalogue and sign-in on
      When an operator's monitor checks the health of the service 3 times, without a token
      Then each check answers healthy, with only a status
      And the health check writes no log entry

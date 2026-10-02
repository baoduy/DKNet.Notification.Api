@PurchaseOrder
Feature: Purchase order lifecycle (manual sample)
  As a caller of the hand-written ManualSample/PurchaseOrder feature
  I want to create, read, update, cancel and delete purchase orders over HTTP
  So that every hand-written layer of the sample is proven end to end

  Background:
    Given the service is running with no Redis connection configured

  Scenario: Creating a purchase order persists it and returns its details
    When I create a purchase order for customer "Acme Pte Ltd" with amount 250.00
    Then the response status is 201
    And the purchase order response has customer name "Acme Pte Ltd" and amount 250.00
    And the purchase order response status is "placed"

  Scenario: Creating a purchase order raises PurchaseOrderCreatedEvent
    When I create a purchase order for customer "Acme Pte Ltd" with amount 250.00
    Then a log line reports the purchase order created event was received

  Scenario: Replaying the same idempotency key on create does not create a second order
    When I create a purchase order for customer "Acme Pte Ltd" with amount 250.00 using idempotency key "11111111-1111-1111-1111-111111111111"
    And I replay the same create request with idempotency key "11111111-1111-1111-1111-111111111111"
    Then both responses report the same purchase order id

  Scenario: Getting an unknown purchase order returns 404
    When I get the purchase order with id "99999999-9999-9999-9999-999999999999"
    Then the response status is 404

  Scenario: Listing purchase orders returns every created order
    Given a purchase order exists for customer "Wayne Enterprises" with amount 300.00
    When I list purchase orders
    Then the response status is 200
    And the response includes a purchase order for customer "Wayne Enterprises"

  Scenario: Updating a purchase order changes its amount
    Given a purchase order exists for customer "Globex Corporation" with amount 100.00
    When I update that purchase order's amount to 500.00
    Then the response status is 200
    And the purchase order response has amount 500.00

  Scenario: Cancelling a purchase order succeeds once and fails the second time
    Given a purchase order exists for customer "Initech LLC" with amount 50.00
    When I cancel that purchase order
    Then the response status is 200
    And the purchase order response status is "cancelled"
    When I cancel that purchase order again
    Then the response status is 409

  Scenario: Deleting a purchase order removes it
    Given a purchase order exists for customer "Umbrella Corp" with amount 75.00
    When I delete that purchase order
    Then the response status is 200
    When I get that purchase order
    Then the response status is 404

  Scenario Outline: Creating a purchase order rejects invalid input
    When I create a purchase order for customer "<customerName>" with amount <amount>
    Then the response status is 400

    Examples:
      | customerName | amount |
      |               | 100.00 |
      | Acme Pte Ltd | 0      |
      | Acme Pte Ltd | -5     |

  Scenario: Creating a purchase order without an idempotency key is rejected
    When I create a purchase order for customer "Acme Pte Ltd" with amount 100.00 without an idempotency key
    Then the request is rejected

@Product
Feature: Product CRUD lifecycle (automated sample)
  As a caller of the generator-driven AutomatedSample/Product feature
  I want to create, read, update and delete products over HTTP
  So that the fully generated CRUD slice ([CrudCreate]/[CrudUpdate]/[RaisesEvent]/[GenerateDto]) is proven end to end

  Background:
    Given the service is running with no Redis connection configured

  Scenario: Creating a product persists it and returns its details
    When I create a product named "Widget" with price 9.99
    Then the response status is 201
    And the product response has name "Widget" and price 9.99

  Scenario: Creating a product raises the internal ProductCreatedEvent
    When I create a product named "Widget" with price 9.99
    Then a log line reports the automated sample product was created

  Scenario: A negative price is still accepted — a known and accepted limitation of the generated path
    # docs/samples/manual-vs-automated.md's sharpest documented gap: [Range(0.01, double.MaxValue)] is
    # forwarded onto the generated request but never evaluated, because the .NET 10 dknet.notification-API validation
    # source generator cannot see through DKNet.AspCore.Extensions' generic MapPost<TRequest,TResponse>.
    When I create a product named "Broken Widget" with price -1
    Then the response status is 201

  Scenario: Getting an unknown product returns 404
    When I get the product with id "99999999-9999-9999-9999-999999999999"
    Then the response status is 404

  Scenario: Listing products returns every created product
    Given a product exists named "Gizmo" with price 15.00
    When I list products
    Then the response status is 200
    And the response includes a product named "Gizmo"

  Scenario: Updating a product changes its price
    Given a product exists named "Gadget" with price 20.00
    When I change that product's price to 30.00
    Then the response status is 200
    And the product response has price 30.00

  Scenario: Deleting a product removes it
    # DRK-1410: a product must be discontinued before it can be deleted — this scenario now discontinues
    # first, so it keeps proving the generic delete route while staying compatible with that new rule.
    Given a product exists named "Doohickey" with price 5.00
    When I discontinue that product and name "Doohickey Replacement" priced 6.00 as its replacement
    Then the response status is 200
    When I delete that product
    # The generic MapDeleteById<TEntity,TKey,TRequest>() library route returns 204 (No Content) — unlike the manual
    # sample's hand-written delete route, which returns 200 with a body (see PurchaseOrder.feature).
    Then the response status is 204
    When I get that product
    Then the response status is 404

  Scenario: Approving a product stamps the acting user and returns its details
    Given a product exists named "Approvable" with price 12.00
    When I approve that product as "alice"
    Then the response status is 200
    And the product response has name "Approvable" and price 12.00
    And the product response was approved by "alice"

  Scenario: Discontinuing a product marks it discontinued
    Given a product exists named "Retiring" with price 8.00
    When I discontinue that product and name "Retiring II" priced 10.00 as its replacement
    Then the response status is 200
    And the product response is discontinued

  Scenario: Discontinuing an already-discontinued product is refused
    # DRK-1386 R3: discontinuing now creates a replacement product in the same transaction, so it is no
    # longer a repeatable no-op — repeating it against an already-discontinued product is a domain failure.
    Given a product exists named "Retiring Twice" with price 8.00
    When I discontinue that product and name "Retiring Twice II" priced 10.00 as its replacement
    Then the response status is 200
    When I discontinue that product and name "Retiring Twice III" priced 10.00 as its replacement
    Then the response status is 400

  # DRK-1410 §5: preconditions read stored product data before a generated route accepts a request —
  # a create refuses a name already taken, a delete refuses a product still for sale — both answered 409
  # through the one shared error-response body.

  @integration
  Scenario: A product with a free name is created
    Given catalogue-ops holds the scope "products.write"
    When catalogue-ops creates the product "Widget" priced 9.99 SGD
    Then the response is 201
    And the product is named "Widget"

  @integration
  Scenario: A product name that is already taken is refused
    Given catalogue-ops holds the scope "products.write"
    And the product "Widget" priced 9.99 SGD exists
    When catalogue-ops creates the product "Widget" priced 12.00 SGD
    Then the response is 409
    And exactly 1 product is named "Widget"

  @integration
  Scenario Outline: Every refusal carries the service's standard error body
    Given catalogue-ops holds the scope "products.write"
    And the product "Widget" priced 9.99 SGD exists
    And the product "Gadget" priced 5.00 SGD is for sale
    When catalogue-ops <refused request>
    Then the response body carries a trace identifier
    And the response body carries a code naming the rule that refused

    Examples:
      | refused request                               |
      | creates the product "Widget" priced 12.00 SGD |
      | deletes "Gadget"                               |

  @integration
  Scenario: A discontinued product is deleted
    Given catalogue-ops holds the scope "products.write"
    And the product "Widget" priced 9.99 SGD is discontinued
    When catalogue-ops deletes "Widget"
    Then the response is 204
    And "Widget" is gone

  @integration
  Scenario: A product still for sale is not deleted
    Given catalogue-ops holds the scope "products.write"
    And the product "Gadget" priced 5.00 SGD is for sale
    When catalogue-ops deletes "Gadget"
    Then the response is 409
    And "Gadget" still exists

  @integration
  Scenario Outline: The delete rule refuses nothing else
    Given catalogue-ops holds every scope the operation needs
    And the product "Gadget" priced 5.00 SGD is for sale
    And the purchase order "PO-1001" for "Contoso" exists
    When catalogue-ops calls <operation>
    Then the response is <status>

    Examples:
      | operation                           | status |
      | reading "Gadget" by id               | 200    |
      | approving "Gadget"                   | 200    |
      | deleting the purchase order PO-1001  | 200    |

  @integration
  Scenario: A failed command still answers as it does today
    Given catalogue-ops holds the scope "products.discontinue"
    And the product "Widget" priced 9.99 SGD is discontinued
    When catalogue-ops discontinues "Widget" and names "Widget III" priced 14.00 SGD as its replacement
    Then the response is 400
    And the response body carries a trace identifier

  @integration
  Scenario: A request refused on its own values keeps its status
    Given catalogue-ops is signed in
    When catalogue-ops lists purchase orders with page index -1
    Then the response is 400
    And the response names the field it refused

  @integration
  Scenario: An attribute-declared rule is still not evaluated
    Given catalogue-ops holds the scope "products.write"
    When catalogue-ops creates the product "Broken Widget" priced -1.00 SGD
    Then the response is 201

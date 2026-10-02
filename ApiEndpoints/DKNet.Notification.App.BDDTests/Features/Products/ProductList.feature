@Product @ProductList
Feature: Product list query contract (filter · search · order · page)
  As a caller of the generator-driven Product list route (GET /v1/products)
  I want filtering, searching, ordering and paging over the query string
  So that the generic MapGetList<Product, Guid, ProductDto>() contract stays stable as the feature evolves

  # This feature exists as a regression fence around DKNet.AspCore.Extensions' generic list route — the
  # one MapProductCrud() emits for free. Nothing here is hand-written in the slice, so a future package
  # bump or generator change could silently alter the contract; these scenarios make that loud.
  # Full contract reference: docs/generic-list-endpoint.md.

  Background:
    Given the service is running with no Redis connection configured
    And the following products exist:
      | name    | price |
      | Apple   | 10.00 |
      | Apricot | 20.00 |
      | Banana  | 30.00 |
      | Cherry  | 40.00 |

  # --- Paging & envelope -------------------------------------------------------------------------

  Scenario: A page carries the full envelope metadata, not a bare array
    # WHY: the generated route wraps results in PagedResponse<T>; totalItemCount must be the UNPAGED
    # total (4) even when only 2 rows come back, or clients can't page correctly.
    When I list products with query "?pageSize=2&pageNumber=1"
    Then the response status is 200
    And the list contains exactly 2 products
    And the paged response "totalItemCount" is "4"
    And the paged response "hasNextPage" is "true"
    And the paged response "hasPreviousPage" is "false"

  Scenario: The second page reports it has a previous page and no next page
    # WHY: proves pageNumber actually advances the window and the next/previous flags flip at the ends.
    When I list products with query "?pageSize=2&pageNumber=2"
    Then the response status is 200
    And the list contains exactly 2 products
    And the paged response "hasNextPage" is "false"
    And the paged response "hasPreviousPage" is "true"

  Scenario: pageSize above the max is clamped, not rejected
    # WHY: pageSize is capped at 1000 (DKNet.AspCore.Extensions 10.1.20 ListQueryOptions.MaxPageSize) by
    # silent clamp (not a 400) — a future change to reject instead would break existing callers that
    # over-ask.
    When I list products with query "?pageSize=99999"
    Then the response status is 200
    And the paged response "pageSize" is "1000"

  # --- Ordering ----------------------------------------------------------------------------------

  Scenario: Ordering ascending by a named field
    # WHY: orderBy must sort by the DTO field asc by default. Uses distinct prices so the order is
    # deterministic regardless of key/timestamp resolution.
    When I list products with query "?orderBy=price"
    Then the response status is 200
    And the listed products are in order "Apple, Apricot, Banana, Cherry"

  Scenario: Ordering descending with desc=true
    # WHY: desc must reverse the sort — the most common client need (newest/highest first).
    When I list products with query "?orderBy=price&desc=true"
    Then the response status is 200
    And the listed products are in order "Cherry, Banana, Apricot, Apple"

  # --- Filtering ---------------------------------------------------------------------------------

  Scenario: Filtering by a numeric comparison
    # WHY: proves value coercion to decimal and the GreaterThan operator. Field "price" is lowercase
    # on purpose — the route PascalCases and matches case-insensitively.
    When I list products with query "?filter=price:GreaterThan:20"
    Then the response status is 200
    And the list contains exactly "Banana, Cherry"

  Scenario: Multiple filters combine with AND
    # WHY: repeated filter params must intersect, not union — a bounded range is the canonical case.
    When I list products with query "?filter=price:GreaterThanOrEqual:20&filter=price:LessThan:40"
    Then the response status is 200
    And the list contains exactly "Apricot, Banana"

  Scenario: The In operator matches any of a comma-separated list
    # WHY: In takes a value list, each element coerced to the field's type — a distinct code path from
    # the scalar operators above.
    When I list products with query "?filter=name:In:Apple,Cherry"
    Then the response status is 200
    And the list contains exactly "Apple, Cherry"

  # --- Search ------------------------------------------------------------------------------------

  Scenario: Free-text search matches a substring anywhere in a string field
    # WHY: search is Contains (not StartsWith) across string DTO fields; "rico" sits mid-word in
    # "Apricot" and matches nothing else — proves substring, not prefix.
    When I list products with query "?search=rico"
    Then the response status is 200
    And the list contains exactly "Apricot"

  Scenario: A one-character search is rejected
    # WHY: the 2-char minimum is a deliberate guard against whole-table scans; loosening it silently
    # would be a performance regression.
    When I list products with query "?search=a"
    Then the response status is 400

  # --- DTO boundary (security) -------------------------------------------------------------------

  Scenario: Filtering by a field the DTO does not expose is rejected
    # WHY: unknown fields must 400, never be silently dropped (which would return an unfiltered page a
    # caller believes is filtered).
    When I list products with query "?filter=colour:Equal:red"
    Then the response status is 400

  Scenario: Filtering by an excluded column is rejected, not honoured
    # WHY: THE key regression fence. ProductDto excludes OwnedBy ([GenerateDto(... Exclude ...)]), so the
    # ownership key must be unqueryable over HTTP. If a future change widened the query surface to the raw
    # entity, this would start returning 200 and leak a filter on a hidden column — this scenario fails loud.
    When I list products with query "?filter=ownedBy:Equal:someone"
    Then the response status is 400

  # --- Recent-activity window (fromDate / toDate, DKNet.AspCore.Extensions 10.1.20) ---------------

  Scenario: A fromDate at or below the records' creation moment returns them
    # WHY: fromDate is an inclusive lower bound on last-activity — a window opening before the
    # Background products were seeded must still return all of them.
    When I list products with a fromDate 1 hours in the past
    Then the response status is 200
    And the list contains exactly 4 products
    And the paged response "totalItemCount" is "4"

  Scenario: A toDate below the records' creation moment excludes them
    # WHY: toDate is an inclusive upper bound — a window that closes before the Background products
    # were seeded must exclude every one of them, reporting an empty page rather than a 404.
    When I list products with a toDate 1 hours in the past
    Then the response status is 200
    And the list contains exactly 0 products
    And the paged response "totalItemCount" is "0"

  Scenario: An impossible window is refused
    # WHY: fromDate later than toDate can never match anything — the package rejects it outright (400)
    # rather than silently returning an empty page.
    When I list products with a fromDate 1 hours in the future and a toDate 1 hours in the past
    Then the response status is 400

  # --- Customised DTO field (DRK-1430) ------------------------------------------------------------

  Scenario: Sorting by the hand-mapped gross margin field is refused, not a server error
    # WHY (DRK-1430 R3/R5): GrossMargin is a computed DTO field with no entity counterpart. The list
    # route's DTO boundary must still 400 it like any unknown field — never a 500 from a query that fails
    # to translate.
    When I list products with query "?orderBy=grossMargin"
    Then the response status is 400
    And the response names the unsupported field "grossMargin"

  Scenario: Filtering by the hand-mapped gross margin field is refused, not a server error
    When I list products with query "?filter=grossMargin:GreaterThan:0"
    Then the response status is 400
    And the response names the unsupported field "grossMargin"

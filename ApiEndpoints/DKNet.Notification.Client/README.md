# DKNet.Notification.Client

Typed [Refit](https://github.com/reactiveui/refit) client for the DKNet.Notification API: one method per API endpoint,
with its own copies of the request and response contracts. It references no other project of the solution.

## Install

```bash
dotnet add package DKNet.Notification.Client
```

## Register

One call registers `IProductsClient` and `IPurchaseOrdersClient` against the API's base address:

```csharp
services.AddApiClient(new Uri("https://orders.example.com"));
```

The client attaches no credential of its own. To call an API that requires authentication, pass your own
`DelegatingHandler` — for example one that attaches your bearer token:

```csharp
services.AddApiClient(new Uri("https://orders.example.com"), () => new MyBearerTokenHandler());
```

`AddApiClient` returns the `IHttpClientBuilder` both clients share, so resilience handlers, timeouts or a
primary handler can be configured on it.

## Call

```csharp
var summary = await products.GetProductSummaryAsync(cancellationToken);

var order = await purchaseOrders.CreatePurchaseOrderAsync(
    new CreatePurchaseOrderRequest { CustomerName = "PO-1001", Amount = 100.00m },
    idempotencyKey: "po-1001-a",
    cancellationToken);
```

Creating a purchase order requires an idempotency key, sent as the `X-Idempotency-Key` header. A null or
blank key is refused before any request is sent: the call throws `Refit.ApiRequestException`, whose
`InnerException` is the `ArgumentException` naming the missing header.

## Handle a refused call

When the API refuses a call (4xx or 5xx), the client throws `Refit.ApiException`. Read the status code and
the problem details the API returned:

```csharp
try
{
    await products.ListProductsAsync(new ProductListQuery { Filter = ["colour:Equal:red"] });
}
catch (ApiException ex)
{
    var status = ex.StatusCode;                                          // 400
    var problem = await ex.GetContentAsAsync<ProblemDetailsResponse>();  // problem.Detail explains why
}
```

A response with no body — for example a bare `404 Not Found` — leaves `ex.Content` empty.

using DKNet.Notification.AppServices;

namespace DKNet.Notification.App.Tests.Architecture;

/// <summary>
/// DRK-1430 §5 @unit: "The sample still writes no product route, request or handler by hand." The gross
/// margin customisation is a response-mapping concern only — proving it stayed one means proving no
/// hand-written route, request or handler was added for it anywhere in the generated Product operations.
/// </summary>
public class ProductGrossMarginStructureTests
{
    [Fact]
    public void ProductV1Endpoint_DeclaresNoHandWrittenRouteOrReferenceForGrossMargin()
    {
        var path = Path.Combine(SrcDir, "ApiEndpoints/DKNet.Notification.Api/ApiEndpoints/AutomatedSample/ProductV1Endpoint.cs");
        var content = File.ReadAllText(path);

        content.Contains("GrossMargin", StringComparison.OrdinalIgnoreCase).ShouldBeFalse(
            "ProductV1Endpoint must stay untouched by the gross-margin customisation — no hand-written " +
            "route or reference to it belongs in the endpoint file.");
    }

    [Fact]
    public void NoHandWrittenRequestOrHandlerType_ExistsForGrossMargin()
    {
        var offenders = typeof(AppSetup).Assembly.GetTypes()
            .Where(t => t.Name.Contains("GrossMargin", StringComparison.OrdinalIgnoreCase))
            .Where(t => t.Name.EndsWith("Request", StringComparison.Ordinal) ||
                        t.Name.EndsWith("Handler", StringComparison.Ordinal))
            .Select(t => t.FullName)
            .ToArray();

        offenders.ShouldBeEmpty(
            $"the gross margin value must reach the response through the DTO mapping alone — found a " +
            $"hand-written request/handler type: {string.Join(", ", offenders)}");
    }

    private static string SrcDir =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../.."));
}

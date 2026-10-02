using System.Text.RegularExpressions;
using DKNet.Notification.AppServices;

namespace DKNet.Notification.App.Tests.Architecture;

/// <summary>
/// Structural invariants unique to the DRK-711 two-sample cycle (see docs/samples/manual-vs-automated.md):
/// the manual sample (<c>ManualSample</c>/<c>PurchaseOrder</c>) must stay 100% hand-written — no declarative
/// event/CRUD/DTO-generation attribute anywhere under it — and the automated sample
/// (<c>AutomatedSample</c>/<c>Product</c>) must stay 100% declarative — no hand-written <c>AddEvent</c> call
/// anywhere under it. Also covers the cycle's other named structural checks: no local LazyMapper copy,
/// the generator package reference, generated-code coverage exclusion, and the single migration baseline.
/// </summary>
public class SampleInvariantTests
{
    #region Methods

    [Fact]
    public void ManualSample_ShouldNotUseAnyDeclarativeGenerationAttribute()
    {
        var offenders = SourceFilesUnder("ManualSample")
            .Where(f => ContainsAny(File.ReadAllText(f), "[RaisesEvent", "[CrudCreate]", "[CrudUpdate]", "[CrudAction", "[GenerateDto"))
            .ToArray();

        offenders.ShouldBeEmpty(
            $"ManualSample must stay 100% hand-written — found a declarative generation attribute in: {string.Join(", ", offenders)}");
    }

    [Fact]
    public void AutomatedSample_ShouldNotRaiseEventsByHand()
    {
        var offenders = SourceFilesUnder("AutomatedSample")
            .Where(f => File.ReadAllText(f).Contains("AddEvent(", StringComparison.Ordinal))
            .ToArray();

        offenders.ShouldBeEmpty(
            $"AutomatedSample must declare events only via [RaisesEvent] — found a hand-written AddEvent(...) call in: {string.Join(", ", offenders)}");
    }

    [Fact]
    public void SampleAreas_ShouldNotCrossReferenceEachOther()
    {
        var manualOffenders = SourceFilesUnder("ManualSample")
            .Where(f => File.ReadAllText(f).Contains("AutomatedSample", StringComparison.Ordinal))
            .ToArray();
        var automatedOffenders = SourceFilesUnder("AutomatedSample")
            .Where(f => File.ReadAllText(f).Contains("ManualSample", StringComparison.Ordinal))
            .ToArray();

        manualOffenders.ShouldBeEmpty(
            $"ManualSample must not reference AutomatedSample: {string.Join(", ", manualOffenders)}");
        automatedOffenders.ShouldBeEmpty(
            $"AutomatedSample must not reference ManualSample: {string.Join(", ", automatedOffenders)}");
    }

    [Fact]
    public void NoLocalLazyMapperCopy_ShouldExist()
    {
        var offenders = new[] { "LazyMap.cs", "LazyResult.cs", "LazyMapExtensions.cs" }
            .SelectMany(f => Directory.GetFiles(SrcDir, f, SearchOption.AllDirectories))
            .ToArray();

        offenders.ShouldBeEmpty(
            $"The template must depend on DKNet.SlimBus.Extensions.LazyMapper, not a local copy: {string.Join(", ", offenders)}");
    }

    [Fact]
    public void DKNetSlimBusGeneratorsPackage_ShouldBeReferenced()
    {
        var directoryPackagesPath = Path.Combine(SrcDir, "Directory.Packages.props");
        File.Exists(directoryPackagesPath).ShouldBeTrue();

        var content = File.ReadAllText(directoryPackagesPath);
        content.ShouldContain("DKNet.SlimBus.Generators");
    }

    [Fact]
    public void GeneratedCode_ShouldBeExcludedFromCoverageDenominator()
    {
        var runsettingsPath = Path.Combine(SrcDir, "coverage.runsettings");
        File.Exists(runsettingsPath).ShouldBeTrue();

        var content = File.ReadAllText(runsettingsPath);
        content.ShouldContain("*.g.cs");
    }

    [Fact]
    public void ExactlyTwoMigrations_ShouldExist_WithNoRemovedDemoStorage()
    {
        var migrationsDir = Path.Combine(SrcDir, "ApiEndpoints/DKNet.Notification.Infra/Migrations");
        Directory.Exists(migrationsDir).ShouldBeTrue();

        var migrationFiles = Directory.GetFiles(migrationsDir, "*.cs")
            .Where(f => !Path.GetFileName(f).EndsWith("ModelSnapshot.cs", StringComparison.Ordinal))
            .ToArray();

        // Exactly two migrations = InitDb (DRK-714) + AddProductSupplierSensitiveColumns (DRK-1188), each as
        // <Timestamp>_<Name>.cs + its .Designer.cs.
        migrationFiles.Length.ShouldBe(4,
            $"Expected exactly two migrations (4 files: 2 migrations + 2 designers). Found: {string.Join(", ", migrationFiles.Select(Path.GetFileName))}");

        foreach (var file in migrationFiles)
        {
            var content = File.ReadAllText(file);
            content.Contains("CustomerProfile", StringComparison.OrdinalIgnoreCase).ShouldBeFalse(
                $"{Path.GetFileName(file)} still references the removed CustomerProfile demo feature.");
            content.Contains("LoyaltyMembership", StringComparison.OrdinalIgnoreCase).ShouldBeFalse(
                $"{Path.GetFileName(file)} still references the removed LoyaltyMembership demo feature.");
        }
    }

    [Fact]
    public void NoRemovedDemoEntityNames_ShouldAppearAnywhereUnderSrc()
    {
        var selfPath = Path.Combine(SrcDir, "ApiEndpoints/DKNet.Notification.App.Tests/Architecture/SampleInvariantTests.cs");

        var offenders = Directory.GetFiles(SrcDir, "*", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Where(f => !string.Equals(f, selfPath, StringComparison.Ordinal))
            .Where(f => ContainsAny(SafeReadAllText(f), "CustomerProfile", "LoyaltyMembership"))
            .ToArray();

        offenders.ShouldBeEmpty(
            $"Removed demo entity name found outside SampleInvariantTests: {string.Join(", ", offenders)}");
    }

    [Fact]
    public void AutomatedSample_ShouldHandWriteOnlyTheDroppedAction()
    {
        // DRK-1386: discontinuing a product is the one generated route dropped by name and hand-written
        // (R1) — its rule now spans two aggregates in a single transaction, which no generated handler can
        // express. Approve/AssignSupplierReference must stay fully generated. R7: the hand-written
        // replacement must not reuse the generated type names it drops — DiscontinueProductRequest and
        // DiscontinueProductHandler stay reserved for the generator even after the route is excluded by
        // name (see ManualSample/PurchaseOrder/Actions/Cancel.cs for the *CommandHandler naming this repo's
        // hand-written slices use instead).
        var offenders = SourceFilesUnder("AutomatedSample")
            .Where(f => ContainsAny(File.ReadAllText(f),
                "ApproveProductRequest", "AssignSupplierReferenceProductRequest",
                "ApproveProductCommandHandler", "ApproveProductHandler",
                "AssignSupplierReferenceProductCommandHandler", "AssignSupplierReferenceProductHandler",
                "DiscontinueProductRequest", "DiscontinueProductHandler"))
            .ToArray();

        offenders.ShouldBeEmpty(
            $"Approve/AssignSupplierReference must stay generator-produced, and the hand-written discontinue " +
            $"replacement must not reuse a generated type name — found an offender in: {string.Join(", ", offenders)}");
    }

    [Fact]
    public void ProductV1Endpoint_ShouldDeclareGeneratedRoutesFirst()
    {
        var path = Path.Combine(SrcDir, "ApiEndpoints/DKNet.Notification.Api/ApiEndpoints/AutomatedSample/ProductV1Endpoint.cs");
        var content = File.ReadAllText(path);

        var generatedIndex = content.IndexOf("group.MapProductCrud(", StringComparison.Ordinal);
        generatedIndex.ShouldBeGreaterThanOrEqualTo(0,
            "ProductV1Endpoint must still call the generated MapProductCrud() extension.");

        var handWrittenIndexes = Regex.Matches(content, @"group\.Map(Post|Get|Put|Delete)\(")
            .Select(m => m.Index)
            .ToArray();
        handWrittenIndexes.ShouldNotBeEmpty(
            "expected at least one hand-written route mapped below the generated CRUD block.");

        generatedIndex.ShouldBeLessThan(handWrittenIndexes.Min(),
            "every generated route must be declared above every hand-written route.");
    }

    [Fact]
    public void ProductV1Endpoint_ShouldDropExactlyOneGeneratedRouteByName()
    {
        var path = Path.Combine(SrcDir, "ApiEndpoints/DKNet.Notification.Api/ApiEndpoints/AutomatedSample/ProductV1Endpoint.cs");
        var content = File.ReadAllText(path);

        // A route name is dropped via CrudMapOptions.Exclude("RouteName") — a CrudOp-kind exclusion (an
        // enum member, never a string literal, e.g. Exclude(CrudOp.Action)) drops a whole operation kind,
        // not one named route, and must not count here.
        var excludedByNameCount = Regex.Matches(content, @"\.Exclude\(\s*""[A-Za-z]+""\s*\)").Count;

        excludedByNameCount.ShouldBe(1,
            $"expected exactly one generated route excluded by name, found {excludedByNameCount}.");
    }

    [Fact]
    public void ProductV1Endpoint_HandWrittenReplacement_ShouldStateTheRule()
    {
        // Anchored on the hand-written route's own map call (its route literal contains "discontinue"), not
        // on the first occurrence of the word anywhere in the file — the generated block's
        // o.Exclude("Discontinue") sits above it and would otherwise be mistaken for the route this
        // scenario is about, letting a Build that comments the exclusion but leaves the hand-written route
        // bare pass here while still failing the spec.
        var path = Path.Combine(SrcDir, "ApiEndpoints/DKNet.Notification.Api/ApiEndpoints/AutomatedSample/ProductV1Endpoint.cs");
        var lines = File.ReadAllLines(path);

        var routeLineIndex = Array.FindIndex(lines, l =>
            Regex.IsMatch(l, @"group\.Map(Post|Get|Put|Delete)\(") &&
            l.Contains("discontinue", StringComparison.OrdinalIgnoreCase));
        routeLineIndex.ShouldBeGreaterThanOrEqualTo(0,
            "expected a hand-written route (group.MapPost/Get/Put/Delete) whose route literal contains \"discontinue\".");

        // Walk upward collecting the comment block immediately above that call — stops at the first line
        // that isn't a `//` comment, so a bare route (no comment directly attached) yields no comment lines
        // even if some earlier, unrelated line in the file happens to contain "//".
        var commentLines = new List<string>();
        for (var i = routeLineIndex - 1; i >= 0; i--)
        {
            var trimmed = lines[i].Trim();
            if (!trimmed.StartsWith("//", StringComparison.Ordinal))
            {
                break;
            }

            commentLines.Insert(0, trimmed);
        }

        commentLines.ShouldNotBeEmpty(
            "expected a comment immediately above the hand-written discontinue route's own map call.");

        // R4: the comment must state a rule a reader can apply to their own operations (multi-aggregate
        // transactions in general), not merely a fact about discontinue specifically.
        var comment = string.Join(' ', commentLines);
        comment.Contains("transaction", StringComparison.OrdinalIgnoreCase).ShouldBeTrue(
            "the comment must state the rule generally (e.g. an operation spanning multiple aggregates in " +
            "one transaction cannot be generated), not just describe discontinue.");
    }

    [Fact]
    public void Docs_ShouldNotClaimTheOldShape()
    {
        // DRK-1386 R5: the product sample stops being "all generated" — these phrases described the old
        // shape (no per-route setting, no per-route exclusion) and are now false. docs-writer's sibling
        // [D1386-1] Docs sub-task rewrites the prose; this only guards that no scanned file still makes the
        // stale claim, wherever it appears.
        var selfPath = Path.Combine(SrcDir, "ApiEndpoints/DKNet.Notification.App.Tests/Architecture/SampleInvariantTests.cs");
        var repoRoot = Path.GetFullPath(Path.Combine(SrcDir, ".."));

        string[] staleClaims =
        [
            "nothing hand-mapped",
            "passes no options",
            "only capability",
            "no per-method exclusion",
            "all-or-nothing",
            // DRK-1410: every phrase below claimed a generated route — create, delete, or a domain
            // action — has nowhere to attach a precondition. All four are wrong for the same reason: a
            // generated action's request has always been body-bound and reachable by the group-level
            // FluentValidation filter (DKNet.Notification.Api/Program.cs:50); only delete needed the package's new
            // request binding. The two new validators (create, delete) prove the claim false everywhere.
            "nowhere to hang a",
            "no place to fail a pre-condition first",
            "has nowhere to say no",
            "A business rule that conditionally blocks an operation"
        ];

        var scannedFiles = new[] { "docs", ".claude", ".github" }
            .Select(dir => Path.Combine(repoRoot, dir))
            .Where(Directory.Exists)
            .SelectMany(dir => Directory.GetFiles(dir, "*.md", SearchOption.AllDirectories))
            .Concat(new[] { "README.md", "CLAUDE.md", "AGENTS.md" }
                .Select(file => Path.Combine(repoRoot, file))
                .Where(File.Exists))
            .Where(f => !string.Equals(f, selfPath, StringComparison.Ordinal));

        var offenders = scannedFiles
            .Where(f => ContainsAny(SafeReadAllText(f), staleClaims))
            .ToArray();

        offenders.ShouldBeEmpty(
            $"Found a document still describing the old product-sample shape: {string.Join(", ", offenders)}");
    }

    [Fact]
    public void Docs_ShouldStateWhatTheUniquenessCheckIsNot()
    {
        // DRK-1410: the uniqueness rule is a check, not a guarantee — two callers can pass it at the same
        // moment — so the sample must name the database constraint as what actually keeps the name unique,
        // and must keep stating that an attribute-declared rule on a generated request is not evaluated.
        var repoRoot = Path.GetFullPath(Path.Combine(SrcDir, ".."));
        var readmePath = Path.Combine(repoRoot, "docs", "samples", "automated-products", "README.md");

        // The template's content root is `src/` — `docs/` never reaches a scaffolded consumer solution
        // (see CLAUDE.md's "AGENTS.md ships to consumers" section), so this guard has nothing to check
        // there. Mirrors Docs_ShouldNotClaimTheOldShape's Directory.Exists/File.Exists guards above.
        if (!File.Exists(readmePath))
        {
            return;
        }

        var content = File.ReadAllText(readmePath);

        content.Contains("constraint", StringComparison.OrdinalIgnoreCase).ShouldBeTrue(
            "the sample must name the database constraint as what keeps a product name unique.");
        content.Contains("unique", StringComparison.OrdinalIgnoreCase).ShouldBeTrue(
            "the sample must state that the product name is unique.");
        content.Contains("never evaluated", StringComparison.Ordinal).ShouldBeTrue(
            "the sample must keep stating that an attribute-declared rule on a generated request is not evaluated.");
    }

    [Fact]
    public void CreateAndDeleteProduct_ShouldStayGenerated()
    {
        // DRK-1410: both preconditions attach through validators only — creating and deleting a product
        // must stay fully generated, with no hand-written request or handler for either operation. Matches
        // a declaration (the `record`/`class` keyword immediately before the type name), never a bare
        // occurrence — otherwise the validators the change set adds (rows 3-4: CreateProductRequestValidator,
        // DeleteProductRequestValidator) would close this gate on themselves.
        var declarationPattern = new Regex(@"\b(record|class)\s+(Create|Delete)ProductRequest\b|\b(record|class)\s+(Create|Delete)ProductHandler\b");

        var offenders = SourceFilesUnder("AutomatedSample")
            .Where(f => declarationPattern.IsMatch(File.ReadAllText(f)))
            .ToArray();

        offenders.ShouldBeEmpty(
            $"Creating and deleting a product must stay generated — found a hand-written declaration in: {string.Join(", ", offenders)}");
    }

    [Fact]
    public void GeneratedCreateProductRequest_ShouldCarryNoActingUserProperty()
    {
        // Structural half of the security acceptance criterion (DRK-715 R1): the generated create request
        // has no property a caller could set to claim a different acting user — CreatedBy is stamped only
        // by DataOwnerHook, from the authenticated principal, at save time.
        var requestType = typeof(AppSetup).Assembly.GetTypes().SingleOrDefault(t => t.Name == "CreateProductRequest");

        requestType.ShouldNotBeNull(
            "CreateProductRequest is generated at build time (DKNet.Notification.AppServices.Crud, DKNet.SlimBus.Generators) — build the solution first.");

        requestType!.GetProperties()
            .Select(p => p.Name)
            .ShouldNotContain(n =>
                n.Contains("User", StringComparison.OrdinalIgnoreCase) ||
                n.Contains("CreatedBy", StringComparison.OrdinalIgnoreCase) ||
                n.Contains("UpdatedBy", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void GeneratedDeleteProductRequest_ShouldCarryTheRouteBoundKey()
    {
        // DKNet.SlimBus.Generators 10.1.25+ emits a Delete{Entity}Request for every generated entity
        // (unless a create/update/action member already claims the name); the three-argument
        // MapDeleteById<TEntity,TKey,TRequest>() overload binds Id from the route.
        var requestType = typeof(AppSetup).Assembly.GetTypes().SingleOrDefault(t => t.Name == "DeleteProductRequest");

        requestType.ShouldNotBeNull(
            "DeleteProductRequest is generated at build time (DKNet.Notification.AppServices.Crud, DKNet.SlimBus.Generators) — build the solution first.");

        var idProperty = requestType!.GetProperties().SingleOrDefault(p => p.Name == "Id");

        idProperty.ShouldNotBeNull("DeleteProductRequest must expose a route-bound Id property.");
        idProperty!.PropertyType.ShouldBe(typeof(Guid));
    }

    private static string SrcDir =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../.."));

    private static string SafeReadAllText(string path)
    {
        try
        {
            return File.ReadAllText(path);
        }
        catch (IOException)
        {
            return string.Empty;
        }
        catch (UnauthorizedAccessException)
        {
            return string.Empty;
        }
    }

    private static bool ContainsAny(string haystack, params string[] needles) =>
        needles.Any(n => haystack.Contains(n, StringComparison.Ordinal));

    private static IEnumerable<string> SourceFilesUnder(string sampleFolderName) =>
        Directory.GetFiles(Path.Combine(SrcDir, "ApiEndpoints"), "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Where(f => f.Contains(sampleFolderName, StringComparison.Ordinal));

    #endregion
}

using DKNet.EfCore.Specifications.Extensions;
using DKNet.EfCore.Specifications.Repositories;
using DKNet.Notification.AppServices.AutomatedSample.V1.Specs;
using DKNet.Notification.Domains.Features.AutomatedSample.Entities;

namespace DKNet.Notification.AppServices.AutomatedSample.V1.Actions;

/// <summary>
/// Command that discontinues a product and creates its named replacement in the same transaction.
/// Hand-written, unlike every other Product mutation, because an operation that writes more than one
/// aggregate in one transaction cannot be generated (see <see cref="Product.Discontinue"/>'s remarks).
/// Named "Command", not "Request" — the generated request/handler type names for this dropped action
/// stay reserved for the generator even after its route is excluded by name (R7).
/// </summary>
public sealed record DiscontinueProductCommand : Fluents.Requests.IWitResponse<ProductDto>
{
    #region Properties

    // Not `required`: this is bound from the request body, which never carries "id" (it comes from the
    // route via `req with { Id = id }`) — `required` would make System.Text.Json reject the body outright.
    public Guid Id { get; init; }

    [Required]
    [StringLength(150)]
    public string ReplacementName { get; init; } = null!;

    [Range(0.01, double.MaxValue)]
    public decimal ReplacementPrice { get; init; }

    #endregion
}

internal sealed class DiscontinueProductCommandValidator : AbstractValidator<DiscontinueProductCommand>
{
    #region Constructors

    public DiscontinueProductCommandValidator()
    {
        RuleFor(a => a.ReplacementName).NotEmpty().Length(1, 150);
        RuleFor(a => a.ReplacementPrice).GreaterThan(0);
    }

    #endregion
}

/// <summary>
/// Discontinues the target product and creates its replacement in one handler call, so both mutations
/// commit in the same <c>SaveChangesAsync</c> (<c>EfAutoSavePostInterceptor</c> auto-saves once after this
/// handler returns). The replacement's <c>CreatedBy</c> comes only from <c>DataOwnerHook</c> at save time —
/// this command never carries an acting-user field for a caller to set (R6).
/// </summary>
internal sealed class DiscontinueProductCommandHandler(IRepositorySpec repository, IMapper mapper)
    : Fluents.Requests.IHandler<DiscontinueProductCommand, ProductDto>
{
    #region Methods

    public async Task<IResult<ProductDto>> OnHandle(
        DiscontinueProductCommand request,
        CancellationToken cancellationToken)
    {
        var product = await repository.FirstOrDefaultAsync(new SpecGetProduct(request.Id), cancellationToken);

        if (product is null)
        {
            return Result.Fail<ProductDto>(new NotFoundError($"The product {request.Id} was not found."));
        }

        if (product.IsDiscontinued)
        {
            return Result.Fail<ProductDto>($"The product {request.Id} is already discontinued.");
        }

        product.Discontinue();

        var replacement = new Product(request.ReplacementName, request.ReplacementPrice);
        await repository.AddAsync(replacement, cancellationToken);

        return mapper.ResultOf<ProductDto>(product);
    }

    #endregion
}

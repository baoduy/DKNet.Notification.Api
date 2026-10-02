using DKNet.EfCore.Specifications.Extensions;
using DKNet.EfCore.Specifications.Repositories;
using DKNet.Notification.AppServices.AutomatedSample.V1.Specs;
using DKNet.Notification.AppServices.Crud;

namespace DKNet.Notification.AppServices.AutomatedSample.V1.Validators;

/// <summary>
/// Refuses a delete while the target product is still for sale (R1, R2: binds to
/// <see cref="DeleteProductRequest" /> only). An unknown id passes here — refusing it would hide the
/// route's own 404 behind a 409 — so the generated handler still answers 404 for it.
/// </summary>
internal sealed class DeleteProductRequestValidator : AbstractValidator<DeleteProductRequest>
{
    #region Constructors

    public DeleteProductRequestValidator(IRepositorySpec repository)
    {
        RuleFor(r => r.Id)
            .MustAsync(async (id, cancellationToken) =>
            {
                var product = await repository.FirstOrDefaultAsync(new SpecGetProduct(id), cancellationToken);
                return product is null || product.IsDiscontinued;
            })
            .WithErrorCode(PreconditionCodes.ProductDeleteWhileForSale)
            .WithMessage("The product is still for sale and cannot be deleted.");
    }

    #endregion
}

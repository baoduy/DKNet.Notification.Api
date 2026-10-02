using DKNet.EfCore.Specifications.Extensions;
using DKNet.EfCore.Specifications.Repositories;
using DKNet.Notification.AppServices.AutomatedSample.V1.Specs;
using DKNet.Notification.AppServices.Crud;

namespace DKNet.Notification.AppServices.AutomatedSample.V1.Validators;

/// <summary>
/// Refuses a create whose name is already taken by a stored product (R1: read through
/// <see cref="IRepositorySpec" />, not from the request alone — a check, not a guarantee, since two
/// callers can pass it at the same moment; the database's unique index is what actually keeps the name
/// unique). No other rule belongs here — price stays unenforced on the generated create route (R5, R7).
/// </summary>
internal sealed class CreateProductRequestValidator : AbstractValidator<CreateProductRequest>
{
    #region Constructors

    public CreateProductRequestValidator(IRepositorySpec repository)
    {
        RuleFor(r => r.Name)
            .MustAsync(async (name, cancellationToken) =>
                !await repository.AnyAsync(new SpecProductByName(name), cancellationToken))
            .WithErrorCode(PreconditionCodes.ProductNameTaken)
            .WithMessage(r => $"The product name '{r.Name}' is already taken.");
    }

    #endregion
}

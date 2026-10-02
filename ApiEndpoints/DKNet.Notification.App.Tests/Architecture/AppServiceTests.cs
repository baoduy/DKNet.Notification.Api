using FluentValidation;
using NetArchTest.Rules;
using DKNet.Notification.AppServices.Share;

namespace DKNet.Notification.App.Tests.Architecture;

public class AppServiceTests
{
    #region Methods

    [Fact]
    public void AllValidatorClassesShouldBeInternalAndSealed()
    {
        // Adjust the assembly name if needed
        var types = Types.InAssembly(typeof(PreconditionCodes).Assembly);

        var result = types
            .That()
            .AreClasses()
            .And().AreNotAbstract()
            .And().Inherit(typeof(AbstractValidator<>))
            .And().DoNotHaveNameStartingWith("CustomerRequestValidator")
            .And().DoNotHaveNameStartingWith("OrderRequestValidator")
            .Should().NotBePublic()
            .And().BeSealed()
            .GetResult();

        result.IsSuccessful.ShouldBeTrue(
            $"These handler classes should be sealed and internal: \n\t{string.Join(", ", (result.FailingTypes ?? []).Select(t => t.Name))}");
    }

    #endregion
}

using System.Text.RegularExpressions;
using FluentValidation;

namespace DKNet.Notification.AppServices.Notifications;

/// <summary>
///     The field rules of a send call (DRK-2013 §3a). No channel list and no template-existence rule: any channel
///     passes this step, and an unknown template is a <see cref="NotificationErrorCodes.TemplateNotFound" /> later.
///     No message echoes a value the caller sent.
/// </summary>
internal sealed partial class SendNotificationValidator : AbstractValidator<SendNotificationRequest>
{
    #region Fields

    private const string Code = NotificationErrorCodes.InvalidRequest;

    #endregion

    #region Constructors

    public SendNotificationValidator()
    {
        RuleFor(r => r.Channel)
            .NotNull().WithErrorCode(Code)
            .Length(1, 50).WithErrorCode(Code);

        RuleFor(r => r.TemplateId)
            .NotNull().WithErrorCode(Code)
            .Length(1, 100).WithErrorCode(Code);

        RuleFor(r => r.Parameters)
            .Cascade(CascadeMode.Stop)
            .NotNull().WithErrorCode(Code)
            .Must(parameters => parameters.Count <= 50)
            .WithMessage("'Parameters' must hold at most 50 entries.").WithErrorCode(Code);

        RuleForEach(r => r.Parameters)
            .Must(parameter => ParameterName().IsMatch(parameter.Key))
            .WithMessage("Each parameter name must be 1 to 64 letters, digits or '_'.").WithErrorCode(Code)
            .Must(parameter => parameter.Value is not null)
            .WithMessage("Each parameter value must be a string.").WithErrorCode(Code)
            .Must(parameter => parameter.Value is null || parameter.Value.Length <= 4000)
            .WithMessage("Each parameter value must be at most 4,000 characters.").WithErrorCode(Code)
            .When(r => r.Parameters is not null);
    }

    #endregion

    #region Methods

    [GeneratedRegex(@"^[A-Za-z0-9_]{1,64}\z", RegexOptions.None, 100)]
    private static partial Regex ParameterName();

    #endregion
}

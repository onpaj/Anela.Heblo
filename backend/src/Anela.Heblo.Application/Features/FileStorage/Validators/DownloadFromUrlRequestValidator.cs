using Anela.Heblo.Application.Features.FileStorage.UseCases.DownloadFromUrl;
using Anela.Heblo.Application.Shared;
using FluentValidation;

namespace Anela.Heblo.Application.Features.FileStorage.Validators;

public class DownloadFromUrlRequestValidator : AbstractValidator<DownloadFromUrlRequest>
{
    public DownloadFromUrlRequestValidator()
    {
        RuleFor(x => x.ContainerName)
            .Must(IsValidContainerName)
            .WithErrorCode(((int)ErrorCodes.InvalidContainerName).ToString())
            .WithState(x => (object)new Dictionary<string, string>
            {
                { "containerName", x.ContainerName },
                { "cause", "validation" },
            })
            .WithMessage("Invalid container name");
    }

    private static bool IsValidContainerName(string containerName)
    {
        if (string.IsNullOrEmpty(containerName) || containerName.Length < 3 || containerName.Length > 63)
            return false;

        if (containerName != containerName.ToLowerInvariant())
            return false;

        if (!char.IsLetterOrDigit(containerName[0]) || !char.IsLetterOrDigit(containerName[^1]))
            return false;

        for (int i = 0; i < containerName.Length; i++)
        {
            var c = containerName[i];
            if (!char.IsLetterOrDigit(c) && c != '-')
                return false;

            if (c == '-' && i < containerName.Length - 1 && containerName[i + 1] == '-')
                return false;
        }

        return true;
    }
}

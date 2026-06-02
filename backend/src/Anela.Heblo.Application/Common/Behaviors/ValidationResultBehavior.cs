using FluentValidation;
using MediatR;
using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Common.Behaviors;

public class ValidationResultBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
    where TResponse : BaseResponse, new()
{
    private readonly IEnumerable<IValidator<TRequest>> _validators;

    public ValidationResultBehavior(IEnumerable<IValidator<TRequest>> validators)
    {
        _validators = validators;
    }

    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        if (!_validators.Any())
        {
            return await next();
        }

        var context = new ValidationContext<TRequest>(request);

        var validationResults = await Task.WhenAll(
            _validators.Select(v => v.ValidateAsync(context, cancellationToken)));

        var failures = validationResults
            .SelectMany(r => r.Errors)
            .Where(e => e is not null)
            .ToList();

        if (!failures.Any())
        {
            return await next();
        }

        var firstFailure = failures.First();

        var errorCode = Enum.TryParse<ErrorCodes>(firstFailure.ErrorCode, out var parsed)
            ? parsed
            : ErrorCodes.ValidationError;

        return new TResponse
        {
            Success = false,
            ErrorCode = errorCode,
            Params = firstFailure.CustomState as Dictionary<string, string>
        };
    }
}

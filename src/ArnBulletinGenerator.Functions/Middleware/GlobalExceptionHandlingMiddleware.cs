using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Middleware;
using Microsoft.Extensions.Logging;
using ArnBulletinGenerator.Functions.Http;

namespace ArnBulletinGenerator.Functions.Middleware;

/// <summary>
///     Catches any exception an HTTP function leaks, logging it and returning a generic 500
///     instead of crashing the isolated worker host.
/// </summary>
public sealed class GlobalExceptionHandlingMiddleware(
    ILogger<GlobalExceptionHandlingMiddleware> logger) : IFunctionsWorkerMiddleware
{
    public async Task Invoke(FunctionContext context, FunctionExecutionDelegate next)
    {
        try
        {
            await next(context);
        }
        catch (OperationCanceledException)
        {
            logger.LogDebug("Operation canceled in {FunctionName}", context.FunctionDefinition.Name);
            throw;
        }
        catch (Exception ex)
        {
            var request = await context.GetHttpRequestDataAsync();
            if (request is not null)
            {
                logger.LogError(ex, "Unhandled exception in {FunctionName}", context.FunctionDefinition.Name);
                var response = await HttpResponseHelper.InternalErrorAsync(
                    request, "An unexpected error occurred. Please try again later.");
                context.GetInvocationResult().Value = response;
                return;
            }

            logger.LogError(ex, "Unhandled exception in {FunctionName} — rethrowing for host retry/poison handling",
                context.FunctionDefinition.Name);
            throw;
        }
    }
}

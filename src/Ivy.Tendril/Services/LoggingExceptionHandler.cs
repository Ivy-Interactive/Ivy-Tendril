using Ivy.Core.Exceptions;
using Microsoft.Extensions.Logging;

namespace Ivy.Tendril.Services;

public class LoggingExceptionHandler(ILogger<LoggingExceptionHandler> logger) : IExceptionHandler
{
    public bool HandleException(Exception exception)
    {
        logger.LogWarning(exception, "Unhandled exception in event handler");
        return true;
    }
}

using System.Net;

namespace FloatingDeskAssistant.Infrastructure.Api;

public sealed class ModelApiException : Exception
{
    public ModelApiException(string message, bool isRetryable, HttpStatusCode? statusCode = null, Exception? innerException = null)
        : base(message, innerException)
    {
        IsRetryable = isRetryable;
        StatusCode = statusCode;
    }

    public bool IsRetryable { get; }

    public HttpStatusCode? StatusCode { get; }
}

namespace ApiGateway.RateLimiting.Core.Exceptions;

public sealed class BusinessRuleException : Exception
{
    public BusinessRuleException(string message) : base(message)
    {
    }
}
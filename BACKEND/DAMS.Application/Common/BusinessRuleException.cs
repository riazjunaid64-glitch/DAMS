namespace DAMS.Application.Common;

/// <summary>
/// A rule the person filling the form can correct: a duplicate name, a price of zero,
/// a completion date before the start. Controllers turn only this into HTTP 400.
/// A database failure or a programming bug is not one of these and must reach
/// <c>ExceptionMiddleware</c>, which logs it and returns a safe 500.
/// </summary>
public sealed class BusinessRuleException : Exception
{
    public BusinessRuleException(string message) : base(message)
    {
    }
}

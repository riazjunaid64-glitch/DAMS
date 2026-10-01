namespace DAMS.Application.Common
{
    /// <summary>A customer document request that clashes with one the customer already has (answered as 409).</summary>
    public class CustomerDocumentConflictException : InvalidOperationException
    {
        public CustomerDocumentConflictException(string message) : base(message) { }
        public CustomerDocumentConflictException(string message, Exception inner) : base(message, inner) { }
    }
}

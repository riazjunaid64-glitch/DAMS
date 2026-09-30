using DAMS.Domain.Enums;

namespace DAMS.Application.Common
{
    /// <summary>What a customer payment must say about how it was made, shared by every way of recording one.</summary>
    internal static class PaymentInputRules
    {
        /// <summary>
        /// The method is always chosen, never assumed — an omitted one used to become Cash silently — and
        /// anything that is not cash leaves a trail to follow: a cheque or transfer number.
        /// </summary>
        public static (PaymentMethod Method, string? Reference) Resolve(PaymentMethod? method, string? reference)
        {
            if (method is null)
                throw new InvalidOperationException("Payment method is required.");

            var trimmed = string.IsNullOrWhiteSpace(reference) ? null : reference.Trim();
            if (method != PaymentMethod.Cash && trimmed is null)
                throw new InvalidOperationException("A reference is required for bank transfer, cheque and online payments.");

            return (method.Value, trimmed);
        }
    }
}

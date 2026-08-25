using DAMS.Application.Interfaces;
using DAMS.Application.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace DAMS.Application.Tests
{
    /// <summary>
    /// An attachment writer for tests that record money movements without any file. It stores
    /// nothing and returns nothing, so a test that never uploads anything is unaffected by the
    /// evidence plumbing — and a test that tries to would fail loudly rather than silently write
    /// into the developer's real storage folder.
    /// </summary>
    internal static class TestAttachments
    {
        public static IFinanceAttachmentWriter Writer() =>
            new FinanceAttachmentWriter(new UnusableStorage(), NullLogger<FinanceAttachmentWriter>.Instance);

        private sealed class UnusableStorage : IFinanceAttachmentStorage
        {
            public Task<string> SaveAsync(Stream content, string extension, CancellationToken cancellationToken = default) =>
                throw new NotSupportedException("This test was not set up to store attachments.");

            public Task<Stream?> OpenReadAsync(string storedFileName, CancellationToken cancellationToken = default) =>
                Task.FromResult<Stream?>(null);

            public Task DeleteAsync(string storedFileName, CancellationToken cancellationToken = default) =>
                Task.CompletedTask;
        }
    }
}

using DAMS.Application.Interfaces;

namespace DAMS.Application.Services
{
    public sealed class PrivateFinanceAttachmentStorage : PrivateFileStorage, IFinanceAttachmentStorage
    {
        public PrivateFinanceAttachmentStorage(string storageRoot) : base(storageRoot)
        {
        }
    }
}

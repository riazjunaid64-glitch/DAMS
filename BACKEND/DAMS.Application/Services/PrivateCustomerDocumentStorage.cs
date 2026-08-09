using DAMS.Application.Interfaces;

namespace DAMS.Application.Services
{
    public sealed class PrivateCustomerDocumentStorage : PrivateFileStorage, ICustomerDocumentStorage
    {
        public PrivateCustomerDocumentStorage(string storageRoot) : base(storageRoot) { }
    }
}

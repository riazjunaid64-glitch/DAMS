using DAMS.Application.Interfaces;

namespace DAMS.Application.Services
{
    public sealed class PrivateLeadDocumentStorage : PrivateFileStorage, ILeadDocumentStorage
    {
        public PrivateLeadDocumentStorage(string storageRoot) : base(storageRoot)
        {
        }
    }
}

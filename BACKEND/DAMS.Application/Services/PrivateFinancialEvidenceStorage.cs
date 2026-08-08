using DAMS.Application.Interfaces;

namespace DAMS.Application.Services
{
    public sealed class PrivateFinancialEvidenceStorage : PrivateFileStorage, IFinancialEvidenceStorage
    {
        public PrivateFinancialEvidenceStorage(string storageRoot) : base(storageRoot) { }
    }
}

using DAMS.Application.Common;
using DAMS.Application.DTOs.CommissionRebateDtos;
using DAMS.Application.DTOs.FinanceDtos;
using DAMS.Domain.Entities;
using DAMS.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace DAMS.Application.Services
{
    public sealed partial class CommissionRebateService
    {
        private static readonly HashSet<string> PartnerTypes = new(StringComparer.OrdinalIgnoreCase)
        {
            "Broker", "Dealer", "Agency", "Referral Partner", "Introducer", "Marketing Partner",
            "External Sales Agent", "Other"
        };

        public async Task<PagedResult<ThirdPartyPartnerDto>> GetPartnersAsync(string? search, bool? isActive,
            int skip, int take,
            CancellationToken cancellationToken = default)
        {
            skip = Math.Max(0, skip);
            take = Math.Clamp(take, 1, 500);
            var query = _context.ThirdPartyPartners.AsNoTracking().AsQueryable();
            if (isActive.HasValue) query = query.Where(p => p.IsActive == isActive.Value);
            if (!string.IsNullOrWhiteSpace(search))
            {
                var term = search.Trim();
                query = query.Where(p => p.Name.Contains(term) || p.InternalCode.Contains(term)
                    || (p.Phone != null && p.Phone.Contains(term)) || (p.Email != null && p.Email.Contains(term))
                    || (p.Cnic != null && p.Cnic.Contains(term)) || (p.Ntn != null && p.Ntn.Contains(term)));
            }
            var rows = await ProjectPartners(query.OrderByDescending(p => p.IsActive).ThenBy(p => p.Name).ThenBy(p => p.Id))
                .Skip(skip).Take(take + 1).ToListAsync(cancellationToken);
            return new PagedResult<ThirdPartyPartnerDto>
            {
                Items = rows.Take(take).ToList(),
                HasMore = rows.Count > take
            };
        }

        public async Task<ThirdPartyPartnerDto> CreatePartnerAsync(SaveThirdPartyPartnerDto dto,
            FinancialWorkflowActor actor, CancellationToken cancellationToken = default)
        {
            ValidatePartner(dto);
            await EnsurePartnerUniqueAsync(dto, null, cancellationToken);
            var partner = new ThirdPartyPartner { CreatedByUserId = actor.UserId, CreatedByName = actor.DisplayName, CreatedAt = DateTime.UtcNow };
            AssignPartner(partner, dto);
            _context.ThirdPartyPartners.Add(partner);
            var audit = Audit(FinancialWorkflowAction.PartnerCreated, actor);
            audit.Partner = partner;
            await _context.SaveChangesAsync(cancellationToken);
            return await GetPartnerAsync(partner.Id, cancellationToken);
        }

        public async Task<ThirdPartyPartnerDto> UpdatePartnerAsync(int id, SaveThirdPartyPartnerDto dto,
            FinancialWorkflowActor actor, CancellationToken cancellationToken = default)
        {
            ValidatePartner(dto);
            var partner = await _context.ThirdPartyPartners.SingleOrDefaultAsync(p => p.Id == id, cancellationToken)
                ?? throw new KeyNotFoundException("Partner not found.");
            ApplyToken(partner, dto.ConcurrencyToken, "partner");
            await EnsurePartnerUniqueAsync(dto, id, cancellationToken);
            AssignPartner(partner, dto);
            partner.UpdatedAt = DateTime.UtcNow;
            Audit(FinancialWorkflowAction.PartnerUpdated, actor, partnerId: id);
            await _context.SaveChangesAsync(cancellationToken);
            return await GetPartnerAsync(id, cancellationToken);
        }

        public async Task<ThirdPartyPartnerDto> SetPartnerStatusAsync(int id, SetPartnerStatusDto dto,
            FinancialWorkflowActor actor, CancellationToken cancellationToken = default)
        {
            var partner = await _context.ThirdPartyPartners.SingleOrDefaultAsync(p => p.Id == id, cancellationToken)
                ?? throw new KeyNotFoundException("Partner not found.");
            ApplyToken(partner, dto.ConcurrencyToken, "partner");
            if (!dto.IsActive && string.IsNullOrWhiteSpace(dto.Reason))
                throw new InvalidOperationException("A reason is required when deactivating a partner.");
            partner.IsActive = dto.IsActive;
            partner.UpdatedAt = DateTime.UtcNow;
            Audit(dto.IsActive ? FinancialWorkflowAction.PartnerActivated : FinancialWorkflowAction.PartnerDeactivated,
                actor, partnerId: id, reason: dto.Reason);
            await _context.SaveChangesAsync(cancellationToken);
            return await GetPartnerAsync(id, cancellationToken);
        }

        public Task<ThirdPartyAttributionDto> SaveAttributionAsync(int? id, SaveThirdPartyAttributionDto dto,
            FinancialWorkflowActor actor, CancellationToken cancellationToken = default) =>
            SerializableAsync(() => SaveAttributionCoreAsync(id, dto, actor, cancellationToken), cancellationToken);

        private async Task<ThirdPartyAttributionDto> SaveAttributionCoreAsync(int? id, SaveThirdPartyAttributionDto dto,
            FinancialWorkflowActor actor, CancellationToken cancellationToken)
        {
            var ownerCount = (dto.LeadId.HasValue ? 1 : 0) + (dto.CustomerId.HasValue ? 1 : 0) + (dto.BookingId.HasValue ? 1 : 0);
            if (ownerCount != 1) throw new InvalidOperationException("Choose exactly one lead, customer, or booking for attribution.");
            if (dto.AllocationPercent <= 0m || dto.AllocationPercent > 100m)
                throw new InvalidOperationException("Allocation must be greater than zero and no more than 100%.");
            var partner = await _context.ThirdPartyPartners.AsNoTracking().SingleOrDefaultAsync(p => p.Id == dto.PartnerId, cancellationToken)
                ?? throw new InvalidOperationException("Partner not found.");
            if (!partner.IsActive) throw new InvalidOperationException("Inactive partners cannot receive new attributions.");
            if (dto.BookingId.HasValue && !await _context.Bookings.AnyAsync(b => b.Id == dto.BookingId, cancellationToken))
                throw new InvalidOperationException("Booking not found.");
            if (dto.CustomerId.HasValue && !await _context.Customers.AnyAsync(c => c.Id == dto.CustomerId, cancellationToken))
                throw new InvalidOperationException("Customer not found.");
            if (dto.LeadId.HasValue && !await _context.Leads.AnyAsync(l => l.Id == dto.LeadId, cancellationToken))
                throw new InvalidOperationException("Lead not found.");

            ThirdPartyAttribution attribution;
            var action = FinancialWorkflowAction.PartnerAssigned;
            if (id.HasValue)
            {
                attribution = await _context.ThirdPartyAttributions.SingleOrDefaultAsync(a => a.Id == id, cancellationToken)
                    ?? throw new KeyNotFoundException("Attribution not found.");
                ApplyToken(attribution, dto.ConcurrencyToken, "attribution");
                var isReferenced = await _context.BookingCommissions.AnyAsync(c => c.AttributionId == attribution.Id, cancellationToken);
                if (isReferenced && (attribution.PartnerId != dto.PartnerId || attribution.BookingId != dto.BookingId
                    || attribution.CustomerId != dto.CustomerId || attribution.LeadId != dto.LeadId))
                    throw new InvalidOperationException("A referenced attribution cannot be reassigned to another owner or partner. Add a new attribution to preserve commission history.");
                action = FinancialWorkflowAction.PartnerAttributionUpdated;
            }
            else
            {
                attribution = new ThirdPartyAttribution { AssignedAt = DateTime.UtcNow, AssignedByUserId = actor.UserId, AssignedByName = actor.DisplayName };
                _context.ThirdPartyAttributions.Add(attribution);
            }
            if (await _context.ThirdPartyAttributions.AnyAsync(a => a.Id != (id ?? 0) && a.PartnerId == dto.PartnerId
                && a.LeadId == dto.LeadId && a.CustomerId == dto.CustomerId && a.BookingId == dto.BookingId, cancellationToken))
                throw new InvalidOperationException("This partner is already attributed to the selected record.");
            if (dto.IsPrimary && await _context.ThirdPartyAttributions.AnyAsync(a => a.Id != (id ?? 0) && a.IsPrimary
                && a.BookingId == dto.BookingId && a.CustomerId == dto.CustomerId && a.LeadId == dto.LeadId, cancellationToken))
                throw new InvalidOperationException("The selected record already has a primary partner.");
            var allocated = await _context.ThirdPartyAttributions.Where(a => a.Id != (id ?? 0)
                    && a.BookingId == dto.BookingId && a.CustomerId == dto.CustomerId && a.LeadId == dto.LeadId)
                .SumAsync(a => (decimal?)a.AllocationPercent, cancellationToken) ?? 0m;
            if (allocated + dto.AllocationPercent > 100m)
                throw new InvalidOperationException($"Partner allocations cannot exceed 100%. {100m - allocated:0.##}% remains.");
            attribution.PartnerId = dto.PartnerId; attribution.LeadId = dto.LeadId; attribution.CustomerId = dto.CustomerId;
            attribution.BookingId = dto.BookingId; attribution.RelationshipType = Required(dto.RelationshipType, "Relationship type", 100);
            attribution.IntroducedAt = dto.IntroducedAt; attribution.SourceDetails = Limited(dto.SourceDetails, "Source details", 1000);
            attribution.Notes = Limited(dto.Notes, "Notes", 2000); attribution.IsPrimary = dto.IsPrimary;
            attribution.AllocationPercent = Money(dto.AllocationPercent);
            Audit(action, actor, partnerId: dto.PartnerId, customerId: dto.CustomerId, bookingId: dto.BookingId);
            await _context.SaveChangesAsync(cancellationToken);
            return await MapAttributionAsync(attribution.Id, cancellationToken);
        }

        public async Task<List<CommissionRuleDto>> GetRulesAsync(bool? isActive, CancellationToken cancellationToken = default)
        {
            var query = _context.CommissionRules.AsNoTracking().AsQueryable();
            if (isActive.HasValue) query = query.Where(r => r.IsActive == isActive.Value);
            return await query.OrderByDescending(r => r.IsActive).ThenByDescending(r => r.Priority).ThenBy(r => r.Name)
                .Take(500).Select(r => new CommissionRuleDto
                {
                    Id = r.Id, Name = r.Name, Description = r.Description, IsActive = r.IsActive,
                    EffectiveFrom = r.EffectiveFrom, EffectiveTo = r.EffectiveTo, PartnerId = r.PartnerId,
                    PartnerName = r.Partner != null ? r.Partner.Name : null, PartnerType = r.PartnerType,
                    ProjectId = r.ProjectId, ProjectName = r.Project != null ? r.Project.ProjectName : null,
                    UnitCategory = r.UnitCategory, BookingSource = r.BookingSource, BookingId = r.BookingId,
                    CalculationType = r.CalculationType, PercentageRate = r.PercentageRate, FixedAmount = r.FixedAmount,
                    CalculationBasis = r.CalculationBasis, MinimumCommission = r.MinimumCommission,
                    MaximumCommission = r.MaximumCommission, EligibilityCondition = r.EligibilityCondition,
                    EarningCondition = r.EarningCondition, MinimumCollectionPercent = r.MinimumCollectionPercent,
                    Priority = r.Priority, RequiresApproval = r.RequiresApproval, Notes = r.Notes,
                    ConcurrencyToken = Convert.ToBase64String(r.RowVersion)
                }).ToListAsync(cancellationToken);
        }

        public Task<CommissionRuleDto> CreateRuleAsync(SaveCommissionRuleDto dto, FinancialWorkflowActor actor,
            CancellationToken cancellationToken = default) => SaveRuleAsync(null, dto, actor, cancellationToken);

        public Task<CommissionRuleDto> UpdateRuleAsync(int id, SaveCommissionRuleDto dto, FinancialWorkflowActor actor,
            CancellationToken cancellationToken = default) => SaveRuleAsync(id, dto, actor, cancellationToken);

        private async Task<CommissionRuleDto> SaveRuleAsync(int? id, SaveCommissionRuleDto dto,
            FinancialWorkflowActor actor, CancellationToken cancellationToken)
        {
            ValidateRule(dto);
            if (dto.PartnerId.HasValue && !await _context.ThirdPartyPartners.AnyAsync(p => p.Id == dto.PartnerId, cancellationToken))
                throw new InvalidOperationException("Partner not found.");
            if (dto.ProjectId.HasValue && !await _context.Projects.AnyAsync(p => p.Id == dto.ProjectId, cancellationToken))
                throw new InvalidOperationException("Project not found.");
            if (dto.BookingId.HasValue && !await _context.Bookings.AnyAsync(b => b.Id == dto.BookingId, cancellationToken))
                throw new InvalidOperationException("Booking not found.");
            CommissionRule rule;
            if (id.HasValue)
            {
                rule = await _context.CommissionRules.SingleOrDefaultAsync(r => r.Id == id, cancellationToken)
                    ?? throw new KeyNotFoundException("Commission rule not found.");
                ApplyToken(rule, dto.ConcurrencyToken, "rule");
            }
            else
            {
                rule = new CommissionRule { CreatedAt = DateTime.UtcNow, CreatedByUserId = actor.UserId, CreatedByName = actor.DisplayName };
                _context.CommissionRules.Add(rule);
            }
            AssignRule(rule, dto); rule.UpdatedAt = id.HasValue ? DateTime.UtcNow : null;
            var audit = Audit(id.HasValue ? FinancialWorkflowAction.RuleUpdated : FinancialWorkflowAction.RuleCreated,
                actor, commissionRuleId: id);
            if (!id.HasValue) audit.CommissionRule = rule;
            await _context.SaveChangesAsync(cancellationToken);
            return (await GetRulesAsync(null, cancellationToken)).Single(r => r.Id == rule.Id);
        }

        private async Task<ThirdPartyAttributionDto> MapAttributionAsync(int id, CancellationToken cancellationToken) =>
            await _context.ThirdPartyAttributions.AsNoTracking().Where(a => a.Id == id).Select(a => new ThirdPartyAttributionDto
            {
                Id = a.Id, PartnerId = a.PartnerId, PartnerName = a.Partner.Name, LeadId = a.LeadId,
                CustomerId = a.CustomerId, BookingId = a.BookingId, RelationshipType = a.RelationshipType,
                IntroducedAt = a.IntroducedAt, SourceDetails = a.SourceDetails, Notes = a.Notes, IsPrimary = a.IsPrimary,
                AllocationPercent = a.AllocationPercent, AssignedAt = a.AssignedAt,
                ConcurrencyToken = Convert.ToBase64String(a.RowVersion)
            }).SingleAsync(cancellationToken);

        private async Task<ThirdPartyPartnerDto> GetPartnerAsync(int id, CancellationToken cancellationToken) =>
            await ProjectPartners(_context.ThirdPartyPartners.AsNoTracking().Where(p => p.Id == id))
                .SingleAsync(cancellationToken);

        private static IQueryable<ThirdPartyPartnerDto> ProjectPartners(IQueryable<ThirdPartyPartner> query) =>
            query.Select(p => new ThirdPartyPartnerDto
            {
                Id = p.Id, Name = p.Name, PartnerType = p.PartnerType, ContactPerson = p.ContactPerson,
                Phone = p.Phone, Email = p.Email, Address = p.Address, Cnic = p.Cnic, Ntn = p.Ntn,
                RegistrationNumber = p.RegistrationNumber, InternalCode = p.InternalCode, BankName = p.BankName,
                AccountTitle = p.AccountTitle, AccountNumber = p.AccountNumber, Iban = p.Iban, Notes = p.Notes,
                IsActive = p.IsActive, AttributionCount = p.Attributions.Count, CommissionCount = p.Commissions.Count,
                CreatedAt = p.CreatedAt, UpdatedAt = p.UpdatedAt, ConcurrencyToken = Convert.ToBase64String(p.RowVersion)
            });

        private static void ValidatePartner(SaveThirdPartyPartnerDto dto)
        {
            Required(dto.Name, "Partner name", 200); Required(dto.PartnerType, "Partner type", 80);
            if (!PartnerTypes.Contains(dto.PartnerType.Trim()))
                throw new InvalidOperationException("Select a valid partner type.");
            Required(dto.InternalCode, "Internal code", 80); Limited(dto.ContactPerson, "Contact person", 200);
            Limited(dto.Phone, "Phone", 50); Limited(dto.Email, "Email", 200); Limited(dto.Address, "Address", 500);
            Limited(dto.Cnic, "CNIC", 50); Limited(dto.Ntn, "NTN", 80); Limited(dto.RegistrationNumber, "Registration number", 100);
            Limited(dto.BankName, "Bank name", 150); Limited(dto.AccountTitle, "Account title", 150);
            Limited(dto.AccountNumber, "Account number", 100); Limited(dto.Iban, "IBAN", 100); Limited(dto.Notes, "Notes", 2000);
        }

        private async Task EnsurePartnerUniqueAsync(SaveThirdPartyPartnerDto dto, int? id, CancellationToken cancellationToken)
        {
            var code = Required(dto.InternalCode, "Internal code", 80).ToUpperInvariant();
            var cnic = Normalize(dto.Cnic); var ntn = Normalize(dto.Ntn);
            var phone = NormalizePhone(dto.Phone); var email = NormalizeEmail(dto.Email);
            if (await _context.ThirdPartyPartners.AnyAsync(p => p.Id != (id ?? 0) && p.InternalCode == code, cancellationToken))
                throw new InvalidOperationException("A partner with this internal code already exists.");
            if (cnic != null && await _context.ThirdPartyPartners.AnyAsync(p => p.Id != (id ?? 0) && p.NormalizedCnic == cnic, cancellationToken))
                throw new InvalidOperationException("A partner with this CNIC already exists.");
            if (ntn != null && await _context.ThirdPartyPartners.AnyAsync(p => p.Id != (id ?? 0) && p.NormalizedNtn == ntn, cancellationToken))
                throw new InvalidOperationException("A partner with this NTN already exists.");
            if (phone != null && email != null && await _context.ThirdPartyPartners.AnyAsync(p => p.Id != (id ?? 0)
                    && p.NormalizedPhone == phone && p.NormalizedEmail == email, cancellationToken))
                throw new InvalidOperationException("A partner with this normalized phone and email already exists.");
        }

        private static void AssignPartner(ThirdPartyPartner p, SaveThirdPartyPartnerDto dto)
        {
            p.Name = Required(dto.Name, "Partner name", 200); p.PartnerType = Required(dto.PartnerType, "Partner type", 80);
            p.ContactPerson = Clean(dto.ContactPerson); p.Phone = Clean(dto.Phone); p.NormalizedPhone = NormalizePhone(dto.Phone);
            p.Email = Clean(dto.Email); p.NormalizedEmail = NormalizeEmail(dto.Email); p.Address = Clean(dto.Address);
            p.Cnic = Clean(dto.Cnic); p.NormalizedCnic = Normalize(dto.Cnic); p.Ntn = Clean(dto.Ntn); p.NormalizedNtn = Normalize(dto.Ntn);
            p.RegistrationNumber = Clean(dto.RegistrationNumber); p.InternalCode = Required(dto.InternalCode, "Internal code", 80).ToUpperInvariant();
            p.BankName = Clean(dto.BankName); p.AccountTitle = Clean(dto.AccountTitle); p.AccountNumber = Clean(dto.AccountNumber);
            p.Iban = Clean(dto.Iban); p.Notes = Clean(dto.Notes);
        }

        private static void ValidateRule(SaveCommissionRuleDto dto)
        {
            Required(dto.Name, "Rule name", 200);
            if (!Enum.IsDefined(dto.CalculationType) || !Enum.IsDefined(dto.CalculationBasis)
                || !Enum.IsDefined(dto.EarningCondition) || (dto.BookingSource.HasValue && !Enum.IsDefined(dto.BookingSource.Value)))
                throw new InvalidOperationException("Select valid rule calculation, basis, earning, and source values.");
            if (!string.IsNullOrWhiteSpace(dto.PartnerType) && !PartnerTypes.Contains(dto.PartnerType.Trim()))
                throw new InvalidOperationException("Select a valid partner type for the rule scope.");
            if (dto.EffectiveFrom == default) throw new InvalidOperationException("Effective-from date is required.");
            if (dto.EffectiveTo < dto.EffectiveFrom) throw new InvalidOperationException("Effective-to date cannot precede effective-from date.");
            if (dto.CalculationType == FinancialCalculationType.Percentage)
            {
                if (dto.PercentageRate is <= 0m or > 100m || dto.FixedAmount.HasValue)
                    throw new InvalidOperationException("Percentage rules require one rate greater than 0 and no fixed amount.");
            }
            else if (dto.FixedAmount is <= 0m || dto.PercentageRate.HasValue)
                throw new InvalidOperationException("Fixed rules require one positive fixed amount and no percentage rate.");
            if (dto.MinimumCommission < 0m || dto.MaximumCommission < 0m || dto.MinimumCommission > dto.MaximumCommission)
                throw new InvalidOperationException("Commission minimum and maximum are invalid.");
            if (dto.EarningCondition == CommissionEarningCondition.MinimumCollectionPercentage
                && dto.MinimumCollectionPercent is not (> 0m and <= 100m))
                throw new InvalidOperationException("A minimum collection percentage between 0 and 100 is required.");
        }

        private static void AssignRule(CommissionRule r, SaveCommissionRuleDto dto)
        {
            r.Name = Required(dto.Name, "Rule name", 200); r.Description = Limited(dto.Description, "Description", 1000);
            r.IsActive = dto.IsActive; r.EffectiveFrom = dto.EffectiveFrom; r.EffectiveTo = dto.EffectiveTo;
            r.PartnerId = dto.PartnerId; r.PartnerType = Limited(dto.PartnerType, "Partner type", 80);
            r.ProjectId = dto.ProjectId; r.UnitCategory = Limited(dto.UnitCategory, "Unit category", 100);
            r.BookingSource = dto.BookingSource; r.BookingId = dto.BookingId; r.CalculationType = dto.CalculationType;
            r.PercentageRate = dto.PercentageRate; r.FixedAmount = dto.FixedAmount.HasValue ? Money(dto.FixedAmount.Value) : null;
            r.CalculationBasis = dto.CalculationBasis; r.MinimumCommission = dto.MinimumCommission.HasValue ? Money(dto.MinimumCommission.Value) : null;
            r.MaximumCommission = dto.MaximumCommission.HasValue ? Money(dto.MaximumCommission.Value) : null;
            r.EligibilityCondition = Limited(dto.EligibilityCondition, "Eligibility condition", 1000);
            r.EarningCondition = dto.EarningCondition; r.MinimumCollectionPercent = dto.MinimumCollectionPercent;
            r.Priority = dto.Priority; r.RequiresApproval = dto.RequiresApproval; r.Notes = Limited(dto.Notes, "Notes", 2000);
        }
    }
}

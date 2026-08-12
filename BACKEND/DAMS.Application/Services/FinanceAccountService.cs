using DAMS.Application.DTOs.FinanceDtos;
using DAMS.Application.Interfaces;
using DAMS.Domain.Entities;
using DAMS.Domain.Enums;
using DAMS.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace DAMS.Application.Services
{
    public sealed class FinanceAccountService : IFinanceAccountService
    {
        private readonly AppDbContext _context;

        public FinanceAccountService(AppDbContext context) => _context = context;

        public async Task<PagedResult<FinanceAccountResponseDto>> GetPageAsync(
            string? search, FinanceAccountType? type, string? holder, bool? isActive,
            int skip, int take, CancellationToken cancellationToken = default)
        {
            var query = _context.FinanceAccounts.AsNoTracking().AsQueryable();
            if (!string.IsNullOrWhiteSpace(search))
            {
                var term = search.Trim();
                query = query.Where(a => a.Name.Contains(term)
                    || a.AccountHolderName.Contains(term)
                    || (a.BankOrWalletName != null && a.BankOrWalletName.Contains(term))
                    || (a.Description != null && a.Description.Contains(term)));
            }
            if (type.HasValue) query = query.Where(a => a.Type == type.Value);
            if (!string.IsNullOrWhiteSpace(holder)) query = query.Where(a => a.AccountHolderName == holder.Trim());
            if (isActive.HasValue) query = query.Where(a => a.IsActive == isActive.Value);

            var rows = await Project(query.OrderByDescending(a => a.IsActive).ThenBy(a => a.Name))
                .Skip(skip).Take(take + 1).ToListAsync(cancellationToken);
            return new PagedResult<FinanceAccountResponseDto>
            {
                Items = rows.Take(take).ToList(),
                HasMore = rows.Count > take
            };
        }

        public Task<List<FinanceAccountOptionDto>> GetOptionsAsync(bool includeInactive, bool cashLikeOnly = true, CancellationToken cancellationToken = default) =>
            _context.FinanceAccounts.AsNoTracking()
                .Where(a => includeInactive || a.IsActive)
                .Where(a => !cashLikeOnly || a.Type == FinanceAccountType.Cash || a.Type == FinanceAccountType.Bank
                    || a.Type == FinanceAccountType.MobileWallet || a.Type == FinanceAccountType.Other)
                .OrderByDescending(a => a.IsActive).ThenBy(a => a.DisplayOrder).ThenBy(a => a.Name)
                .Select(a => new FinanceAccountOptionDto
                {
                    Id = a.Id, Name = a.Name, Type = a.Type,
                    AccountHolderName = a.AccountHolderName, IsActive = a.IsActive
                }).ToListAsync(cancellationToken);

        public async Task<FinanceAccountResponseDto> GetByIdAsync(int id, CancellationToken cancellationToken = default) =>
            await Project(_context.FinanceAccounts.AsNoTracking().Where(a => a.Id == id))
                .SingleOrDefaultAsync(cancellationToken)
            ?? throw new InvalidOperationException("Finance account not found.");

        public async Task<PagedResult<FinanceAccountTransactionDto>> GetTransactionsAsync(
            int id, int skip, int take, CancellationToken cancellationToken = default)
        {
            var account = await _context.FinanceAccounts.AsNoTracking().Where(a => a.Id == id)
                .Select(a => new
                {
                    a.Type,
                    IsTaxPayable = a.Type == FinanceAccountType.Liability && (a.LedgerCode == "11" || a.Name == "Tax Payable")
                }).SingleOrDefaultAsync(cancellationToken)
                ?? throw new InvalidOperationException("Finance account not found.");

            // These projections are unioned below, and EF aligns a union on the FIRST branch's
            // member bindings — a property left unset in this first Select is dropped from every
            // branch and silently returns 0. So every branch binds GrossAmount and WhtAmount, even
            // where they only restate Amount.
            var revenue = _context.ManualRevenues.AsNoTracking().Where(r => r.FinanceAccountId == id)
                .Select(r => new FinanceAccountTransactionDto
                {
                    Kind = "Revenue", RecordId = r.Id, Date = r.Date, Label = r.RevenueType,
                    Reference = r.Reference, ProjectName = r.Project != null ? r.Project.ProjectName : "General",
                    Amount = r.Amount, GrossAmount = r.Amount, WhtAmount = 0m
                });
            // Net, so the running balance in the detail view matches what the bank statement shows.
            var expenses = _context.Expenses.AsNoTracking().Where(e => e.FinanceAccountId == id)
                .Select(e => new FinanceAccountTransactionDto
                {
                    Kind = "Expense", RecordId = e.Id, Date = e.Date, Label = e.Category,
                    Reference = e.Vendor, ProjectName = e.Project != null ? e.Project.ProjectName : "General",
                    Amount = -(e.Amount - e.WhtAmount), GrossAmount = e.Amount, WhtAmount = e.WhtAmount
                });
            var whtDeposits = _context.WhtDeposits.AsNoTracking().Where(d => d.FinanceAccountId == id)
                .Select(d => new FinanceAccountTransactionDto
                {
                    Kind = "WHT deposit", RecordId = d.Id, Date = d.DepositDate, Label = "Tax deposited with FBR",
                    Reference = d.ChallanNumber, ProjectName = "General",
                    Amount = -d.Amount, GrossAmount = d.Amount, WhtAmount = 0m
                });
            var commissionPayouts = _context.CommissionPayouts.AsNoTracking().Where(p => p.FinanceAccountId == id)
                .Select(p => new FinanceAccountTransactionDto
                {
                    Kind = "Commission payout", RecordId = p.Id, Date = p.PaymentDate,
                    Label = p.Commission.Partner.Name, Reference = p.PaymentReference,
                    ProjectName = p.Commission.Booking.Unit.Project.ProjectName,
                    Amount = -p.Amount, GrossAmount = p.Amount, WhtAmount = 0m
                });
            var commissionReversals = _context.CommissionPayoutReversals.AsNoTracking()
                .Where(r => r.Payout.FinanceAccountId == id).Select(r => new FinanceAccountTransactionDto
                {
                    Kind = "Commission reversal", RecordId = r.Id, Date = r.ReversedAt,
                    Label = r.Payout.Commission.Partner.Name, Reference = r.Reason,
                    ProjectName = r.Payout.Commission.Booking.Unit.Project.ProjectName,
                    Amount = r.Amount, GrossAmount = r.Amount, WhtAmount = 0m
                });
            var rebatePayments = _context.RebateDisbursements.AsNoTracking()
                .Where(d => d.FinanceAccountId == id && d.Method == CustomerRebateMethod.CashOrBankPayment)
                .Select(d => new FinanceAccountTransactionDto
                {
                    Kind = "Customer rebate", RecordId = d.Id, Date = d.AppliedAt,
                    Label = d.Rebate.Customer.FullName, Reference = d.Reference,
                    ProjectName = d.Rebate.Booking.Unit.Project.ProjectName,
                    Amount = -d.Amount, GrossAmount = d.Amount, WhtAmount = 0m
                });
            var rebateReversals = _context.RebateDisbursementReversals.AsNoTracking()
                .Where(r => r.Disbursement.FinanceAccountId == id
                    && r.Disbursement.Method == CustomerRebateMethod.CashOrBankPayment)
                .Select(r => new FinanceAccountTransactionDto
                {
                    Kind = "Rebate reversal", RecordId = r.Id, Date = r.ReversedAt,
                    Label = r.Disbursement.Rebate.Customer.FullName, Reference = r.Reason,
                    ProjectName = r.Disbursement.Rebate.Booking.Unit.Project.ProjectName,
                    Amount = r.Amount, GrossAmount = r.Amount, WhtAmount = 0m
                });
            // Customer money in. Bookings on a cancelled sale are kept, matching the Finance
            // dashboard: the cash really did arrive, and cancelling must not rewrite the bank.
            var payments = _context.Payments.AsNoTracking().Where(p => p.FinanceAccountId == id)
                .Select(p => new FinanceAccountTransactionDto
                {
                    Kind = "Customer payment", RecordId = p.Id, Date = p.PaidAt,
                    Label = p.Booking.Customer.FullName, Reference = p.ReceiptNumber,
                    ProjectName = p.Booking.Unit.Project.ProjectName,
                    Amount = p.Amount, GrossAmount = p.Amount, WhtAmount = 0m
                });
            // Tax Payable is the credit side of withholding recorded on supplier expenses. The
            // WhtDeposit.FinanceAccountId is the bank account used, so the payable ledger needs
            // explicit derived rows rather than reusing that cash-account relationship.
            var payableWithheld = _context.Expenses.AsNoTracking().Where(e => account.IsTaxPayable && e.WhtAmount != 0m)
                .Select(e => new FinanceAccountTransactionDto
                {
                    Kind = "WHT withheld", RecordId = e.Id, Date = e.Date, Label = e.Category,
                    Reference = e.Vendor, ProjectName = e.Project != null ? e.Project.ProjectName : "General",
                    Amount = e.WhtAmount, GrossAmount = e.Amount, WhtAmount = e.WhtAmount
                });
            var payableDeposited = _context.WhtDeposits.AsNoTracking().Where(d => account.IsTaxPayable)
                .Select(d => new FinanceAccountTransactionDto
                {
                    Kind = "WHT deposited", RecordId = d.Id, Date = d.DepositDate, Label = "Tax deposited with FBR",
                    Reference = d.ChallanNumber, ProjectName = "General",
                    Amount = -d.Amount, GrossAmount = d.Amount, WhtAmount = 0m
                });
            var capitalCash = _context.CapitalTransactions.AsNoTracking().Where(t => t.FinanceAccountId == id)
                .Select(t => new FinanceAccountTransactionDto
                {
                    Kind = t.Type == CapitalTransactionType.Contribution ? "Capital contribution" : "Capital withdrawal",
                    RecordId = t.Id, Date = t.Date, Label = t.CapitalPartner.Name,
                    Reference = t.Reference, ProjectName = "General",
                    Amount = t.Type == CapitalTransactionType.Contribution ? t.Amount : -t.Amount,
                    GrossAmount = t.Amount, WhtAmount = 0m
                });
            var partnerCapital = _context.CapitalTransactions.AsNoTracking()
                .Where(t => t.CapitalPartner.FinanceAccountId == id)
                .Select(t => new FinanceAccountTransactionDto
                {
                    Kind = t.Type == CapitalTransactionType.OpeningBalance ? "Capital opening balance"
                        : t.Type == CapitalTransactionType.Contribution ? "Capital contribution"
                        : t.Type == CapitalTransactionType.Withdrawal ? "Capital withdrawal"
                        : t.Type == CapitalTransactionType.ProfitShare ? "Capital profit share" : "Capital loss share",
                    RecordId = t.Id, Date = t.Date, Label = t.CapitalPartner.Name,
                    Reference = t.Reference, ProjectName = "General",
                    Amount = t.Type == CapitalTransactionType.Withdrawal || t.Type == CapitalTransactionType.LossShare
                        ? -t.Amount : t.Amount,
                    GrossAmount = t.Amount, WhtAmount = 0m
                });
            var rows = await revenue.Concat(payments).Concat(expenses).Concat(commissionPayouts).Concat(commissionReversals)
                .Concat(rebatePayments).Concat(rebateReversals).Concat(whtDeposits)
                .Concat(capitalCash).Concat(partnerCapital).Concat(payableWithheld).Concat(payableDeposited)
                .OrderByDescending(t => t.Date).ThenByDescending(t => t.RecordId)
                .Skip(skip).Take(take + 1).ToListAsync(cancellationToken);
            return new PagedResult<FinanceAccountTransactionDto>
            {
                Items = rows.Take(take).ToList(), HasMore = rows.Count > take
            };
        }

        public async Task<FinanceAccountsOverviewDto> GetOverviewAsync(CancellationToken cancellationToken = default)
        {
            var accounts = await Project(_context.FinanceAccounts.AsNoTracking()).ToListAsync(cancellationToken);
            return new FinanceAccountsOverviewDto
            {
                ActiveAccounts = accounts.Count(a => a.IsActive),
                InactiveAccounts = accounts.Count(a => !a.IsActive),
                TotalBalance = accounts.Where(a => AccountBalanceDirection.IsCashLike(a.Type)).Sum(a => a.CurrentBalance),
                HolderBalances = accounts.Where(a => AccountBalanceDirection.IsCashLike(a.Type))
                    .GroupBy(a => a.AccountHolderName, StringComparer.OrdinalIgnoreCase)
                    .Select(g => new FinanceHolderBalanceDto
                    {
                        AccountHolderName = g.First().AccountHolderName,
                        AccountCount = g.Count(),
                        CurrentBalance = g.Sum(a => a.CurrentBalance)
                    }).OrderByDescending(h => h.CurrentBalance).ToList()
            };
        }

        public async Task<FinanceAccountResponseDto> CreateAsync(CreateFinanceAccountDto dto, CancellationToken cancellationToken = default)
        {
            Validate(dto);
            if (dto.OpeningBalance != 0m && await _context.OpeningBalanceSets.AnyAsync(cancellationToken))
                throw new InvalidOperationException("Opening balances are controlled in Finance settings. Reopen the baseline to add this amount.");
            await EnsureUniqueName(dto.Name, null, cancellationToken);
            var account = new FinanceAccount
            {
                Name = dto.Name.Trim(), Type = dto.Type,
                AccountHolderName = dto.AccountHolderName.Trim(), OpeningBalance = dto.OpeningBalance,
                LedgerCode = Clean(dto.LedgerCode), DisplayOrder = dto.DisplayOrder,
                BankOrWalletName = Clean(dto.BankOrWalletName), Description = Clean(dto.Description),
                IsActive = true, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow
            };
            _context.FinanceAccounts.Add(account);
            await _context.SaveChangesAsync(cancellationToken);
            return await GetByIdAsync(account.Id, cancellationToken);
        }

        public async Task<FinanceAccountResponseDto> UpdateAsync(int id, UpdateFinanceAccountDto dto, CancellationToken cancellationToken = default)
        {
            Validate(dto);
            var account = await _context.FinanceAccounts.SingleOrDefaultAsync(a => a.Id == id, cancellationToken)
                ?? throw new InvalidOperationException("Finance account not found.");
            ApplyConcurrencyToken(account, dto.ConcurrencyToken);
            if (dto.OpeningBalance != account.OpeningBalance && await _context.OpeningBalanceSets.AnyAsync(cancellationToken))
                throw new InvalidOperationException("Opening balances are controlled in Finance settings. Reopen the baseline to change this amount.");
            if (dto.Type != account.Type && await HasDependenciesAsync(id, cancellationToken))
                throw new InvalidOperationException("An account with financial history cannot change type. Create a correctly typed account and keep this one for reconciliation.");
            await EnsureUniqueName(dto.Name, id, cancellationToken);
            account.Name = dto.Name.Trim();
            account.Type = dto.Type;
            account.AccountHolderName = dto.AccountHolderName.Trim();
            account.OpeningBalance = dto.OpeningBalance;
            account.LedgerCode = Clean(dto.LedgerCode);
            account.DisplayOrder = dto.DisplayOrder;
            account.BankOrWalletName = Clean(dto.BankOrWalletName);
            account.Description = Clean(dto.Description);
            account.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync(cancellationToken);
            return await GetByIdAsync(id, cancellationToken);
        }

        public async Task<FinanceAccountResponseDto> SetActiveAsync(
            int id, bool isActive, string concurrencyToken, CancellationToken cancellationToken = default)
        {
            var account = await _context.FinanceAccounts.SingleOrDefaultAsync(a => a.Id == id, cancellationToken)
                ?? throw new InvalidOperationException("Finance account not found.");
            ApplyConcurrencyToken(account, concurrencyToken);
            account.IsActive = isActive;
            account.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync(cancellationToken);
            return await GetByIdAsync(id, cancellationToken);
        }

        public async Task DeleteUnusedAsync(int id, CancellationToken cancellationToken = default)
        {
            var account = await _context.FinanceAccounts.SingleOrDefaultAsync(a => a.Id == id, cancellationToken)
                ?? throw new InvalidOperationException("Finance account not found.");
            var used = await HasDependenciesAsync(id, cancellationToken);
            if (used) throw new InvalidOperationException("This account has transactions and cannot be deleted. Make it inactive instead.");
            _context.FinanceAccounts.Remove(account);
            await _context.SaveChangesAsync(cancellationToken);
        }

        public async Task EnsureSelectableAsync(int accountId, int? currentAccountId = null, CancellationToken cancellationToken = default)
        {
            var account = await _context.FinanceAccounts.AsNoTracking()
                .Where(a => a.Id == accountId)
                .Select(a => new { a.IsActive, a.Type })
                .SingleOrDefaultAsync(cancellationToken);
            if (account == null) throw new InvalidOperationException("Selected finance account does not exist.");
            if (!account.IsActive && currentAccountId != accountId)
                throw new InvalidOperationException("Selected finance account is inactive. Choose an active account.");
            if (!AccountBalanceDirection.IsCashLike(account.Type))
                throw new InvalidOperationException("Only a cash, bank, mobile wallet or other cash-like account can be used for payments.");
        }

        public async Task<List<FinanceAccountResponseDto>> SetupClientChartAsync(CancellationToken cancellationToken = default)
        {
            var definitions = ClientChart();
            var names = definitions.Select(d => d.Name).ToList();
            var existing = await _context.FinanceAccounts.Where(a => names.Contains(a.Name)).ToListAsync(cancellationToken);
            foreach (var definition in definitions)
            {
                var account = existing.SingleOrDefault(a => string.Equals(a.Name, definition.Name, StringComparison.OrdinalIgnoreCase));
                if (account == null)
                {
                    account = new FinanceAccount
                    {
                        Name = definition.Name, Type = definition.Type, LedgerCode = definition.LedgerCode,
                        DisplayOrder = definition.DisplayOrder, AccountHolderName = definition.Holder,
                        OpeningBalance = 0m, IsActive = true, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow
                    };
                    _context.FinanceAccounts.Add(account);
                    existing.Add(account);
                }
                else if (account.Type != definition.Type)
                {
                    throw new InvalidOperationException($"'{definition.Name}' already exists with account type {account.Type}. Correct it before running setup.");
                }
                else
                {
                    account.LedgerCode ??= definition.LedgerCode;
                    if (account.DisplayOrder == 0) account.DisplayOrder = definition.DisplayOrder;
                }
            }
            await _context.SaveChangesAsync(cancellationToken);

            var partners = await _context.CapitalPartners.Where(p => ClientPartnerNames.Contains(p.Name)).ToListAsync(cancellationToken);
            foreach (var name in ClientPartnerNames)
            {
                var account = existing.Single(a => string.Equals(a.Name, name + " Capital", StringComparison.OrdinalIgnoreCase));
                var partner = partners.SingleOrDefault(p => p.Name == name);
                if (partner == null)
                {
                    _context.CapitalPartners.Add(new CapitalPartner
                    {
                        Name = name, ProfitSharePercent = 11.1111m, FinanceAccountId = account.Id,
                        IsActive = true, CreatedAt = DateTime.UtcNow
                    });
                }
                else if (!partner.FinanceAccountId.HasValue)
                {
                    partner.FinanceAccountId = account.Id;
                }
            }
            await _context.SaveChangesAsync(cancellationToken);
            return await Project(_context.FinanceAccounts.AsNoTracking().Where(a => names.Contains(a.Name)))
                .OrderBy(a => a.DisplayOrder).ThenBy(a => a.Name).ToListAsync(cancellationToken);
        }

        private static readonly string[] ClientPartnerNames =
        [
            "M. Shahid Omer", "Yasir Arfat Anjum", "Nadeem Akhtar Satti", "Imtiaz Raheem",
            "Muhammad Tayyab Khan", "Muhammad Afzal", "Syed Iqbal Mian", "Ayub Satti", "Shabbir Hussain"
        ];

        private static List<ChartAccount> ClientChart()
        {
            var rows = new List<ChartAccount>
            {
                new("HBL Bank", FinanceAccountType.Bank, "4", 10),
                new("HBL Bank 79488961-03", FinanceAccountType.Bank, "4", 20),
                new("Bank Alfalah 1010591891", FinanceAccountType.Bank, "5", 30),
                new("Bank Alfalah 91010840084", FinanceAccountType.Bank, "31", 40),
                new("Bank Alfalah 91010841873", FinanceAccountType.Bank, "31", 50),
                new("Alfalah Saving", FinanceAccountType.Bank, null, 60),
                new("Cash Account", FinanceAccountType.Cash, "6", 70),
                new("Cost of Plot", FinanceAccountType.FixedAsset, "3", 200),
                new("Office Equipment", FinanceAccountType.FixedAsset, "7", 210),
                new("Office Furniture & Fixture", FinanceAccountType.FixedAsset, "23", 220),
                new("Mobile Phone & SIM Cards", FinanceAccountType.FixedAsset, "24", 230),
                new("Floria Building — Work in Progress", FinanceAccountType.WorkInProgress, "26", 300),
                new("Work in Progress — Site Office", FinanceAccountType.WorkInProgress, "29", 310),
                new("Securities & Advances", FinanceAccountType.Receivable, "19", 400),
                new("WHT TAX", FinanceAccountType.Receivable, "37", 410),
                new("Customer General Account / Customer Deposits", FinanceAccountType.Liability, "1", 500),
                new("Tax Payable", FinanceAccountType.Liability, "11", 510),
                new("Loan A/C", FinanceAccountType.Liability, "32", 520)
            };
            rows.AddRange(ClientPartnerNames.Select((name, index) =>
                new ChartAccount(name + " Capital", FinanceAccountType.Capital, null, 600 + index * 10, name)));
            return rows;
        }

        private sealed record ChartAccount(string Name, FinanceAccountType Type, string? LedgerCode, int DisplayOrder, string Holder = "Seven Ventures");

        // Every outflow figure below counts expenses NET of withholding tax. Tax deducted from a
        // supplier never left this account — it is held for FBR, and leaves later as a WhtDeposit,
        // which is why deposits are an outflow here despite not being a business expense.
        //
        // Inflow is customer payments plus manually entered revenue. Payments are the larger of the
        // two by far, so leaving them out does not make the balance approximate — it makes it wrong
        // by the whole of what customers have paid, which is why balances used to read negative.
        private IQueryable<FinanceAccountResponseDto> Project(IQueryable<FinanceAccount> query) =>
            from a in query
            let genericIn = (a.Payments.Sum(p => (decimal?)p.Amount) ?? 0m)
                + (a.ManualRevenues.Sum(r => (decimal?)r.Amount) ?? 0m)
            let genericOut = (a.Expenses.Sum(e => (decimal?)(e.Amount - e.WhtAmount)) ?? 0m)
                + (a.CommissionPayouts.Sum(p => (decimal?)p.Amount) ?? 0m)
                - (a.CommissionPayouts.SelectMany(p => p.Reversals).Sum(r => (decimal?)r.Amount) ?? 0m)
                + (a.RebateDisbursements.Where(d => d.Method == CustomerRebateMethod.CashOrBankPayment)
                    .Sum(d => (decimal?)d.Amount) ?? 0m)
                - (a.RebateDisbursements.Where(d => d.Method == CustomerRebateMethod.CashOrBankPayment)
                    .SelectMany(d => d.Reversals).Sum(r => (decimal?)r.Amount) ?? 0m)
                + (a.WhtDeposits.Sum(d => (decimal?)d.Amount) ?? 0m)
            let cashCapitalIn = a.CapitalCashTransactions.Where(t => t.Type == CapitalTransactionType.Contribution)
                .Sum(t => (decimal?)t.Amount) ?? 0m
            let cashCapitalOut = a.CapitalCashTransactions.Where(t => t.Type == CapitalTransactionType.Withdrawal)
                .Sum(t => (decimal?)t.Amount) ?? 0m
            let partnerIn = a.CapitalPartners.SelectMany(p => p.Transactions)
                .Where(t => t.Type == CapitalTransactionType.OpeningBalance || t.Type == CapitalTransactionType.Contribution
                    || t.Type == CapitalTransactionType.ProfitShare).Sum(t => (decimal?)t.Amount) ?? 0m
            let partnerOut = a.CapitalPartners.SelectMany(p => p.Transactions)
                .Where(t => t.Type == CapitalTransactionType.Withdrawal || t.Type == CapitalTransactionType.LossShare)
                .Sum(t => (decimal?)t.Amount) ?? 0m
            let isTaxPayable = a.Type == FinanceAccountType.Liability && (a.LedgerCode == "11" || a.Name == "Tax Payable")
            let taxPayableIn = isTaxPayable ? (_context.Expenses.Sum(e => (decimal?)e.WhtAmount) ?? 0m) : 0m
            let taxPayableOut = isTaxPayable ? (_context.WhtDeposits.Sum(d => (decimal?)d.Amount) ?? 0m) : 0m
            let debitMovement = genericIn + cashCapitalIn - genericOut - cashCapitalOut
            let movement = a.Type == FinanceAccountType.Capital
                ? partnerIn - partnerOut
                : (isTaxPayable ? taxPayableIn - taxPayableOut : debitMovement)
            select new FinanceAccountResponseDto
            {
                Id = a.Id, Name = a.Name, Type = a.Type, AccountHolderName = a.AccountHolderName,
                OpeningBalance = a.OpeningBalance, LedgerCode = a.LedgerCode, DisplayOrder = a.DisplayOrder,
                BankOrWalletName = a.BankOrWalletName, Description = a.Description, IsActive = a.IsActive,
                RevenueReceived = a.Type == FinanceAccountType.Capital ? partnerIn : (isTaxPayable ? taxPayableIn : genericIn + cashCapitalIn),
                ExpensesPaid = a.Type == FinanceAccountType.Capital ? partnerOut : (isTaxPayable ? taxPayableOut : genericOut + cashCapitalOut),
                WhtWithheld = a.Expenses.Sum(e => (decimal?)e.WhtAmount) ?? 0m,
                WhtDeposited = a.WhtDeposits.Sum(d => (decimal?)d.Amount) ?? 0m,
                NetMovement = movement,
                CurrentBalance = a.OpeningBalance + movement,
                TransactionCount = a.Payments.Count + a.ManualRevenues.Count + a.Expenses.Count + a.CommissionPayouts.Count
                    + a.CommissionPayouts.SelectMany(p => p.Reversals).Count()
                    + a.RebateDisbursements.Count(d => d.Method == CustomerRebateMethod.CashOrBankPayment)
                    + a.RebateDisbursements.Where(d => d.Method == CustomerRebateMethod.CashOrBankPayment)
                        .SelectMany(d => d.Reversals).Count()
                    + a.WhtDeposits.Count + a.CapitalCashTransactions.Count
                    + a.CapitalPartners.SelectMany(p => p.Transactions).Count()
                    + (isTaxPayable ? _context.Expenses.Count(e => e.WhtAmount != 0m) + _context.WhtDeposits.Count() : 0),
                CreatedAt = a.CreatedAt, UpdatedAt = a.UpdatedAt,
                ConcurrencyToken = Convert.ToBase64String(a.RowVersion)
            };

        private async Task EnsureUniqueName(string name, int? excludingId, CancellationToken cancellationToken)
        {
            var normalized = name.Trim();
            if (await _context.FinanceAccounts.AnyAsync(a => a.Name == normalized && (!excludingId.HasValue || a.Id != excludingId), cancellationToken))
                throw new InvalidOperationException("A finance account with this name already exists.");
        }

        private async Task<bool> HasDependenciesAsync(int id, CancellationToken cancellationToken) =>
            await _context.Payments.AnyAsync(p => p.FinanceAccountId == id, cancellationToken)
            || await _context.ManualRevenues.AnyAsync(r => r.FinanceAccountId == id, cancellationToken)
            || await _context.Expenses.AnyAsync(e => e.FinanceAccountId == id, cancellationToken)
            || await _context.CommissionPayouts.AnyAsync(p => p.FinanceAccountId == id, cancellationToken)
            || await _context.RebateDisbursements.AnyAsync(d => d.FinanceAccountId == id, cancellationToken)
            || await _context.WhtDeposits.AnyAsync(d => d.FinanceAccountId == id, cancellationToken)
            || await _context.OpeningBalanceEntries.AnyAsync(e => e.FinanceAccountId == id, cancellationToken)
            || await _context.CapitalPartners.AnyAsync(p => p.FinanceAccountId == id, cancellationToken)
            || await _context.CapitalTransactions.AnyAsync(t => t.FinanceAccountId == id, cancellationToken);

        private static void Validate(CreateFinanceAccountDto dto)
        {
            if (string.IsNullOrWhiteSpace(dto.Name)) throw new InvalidOperationException("Account name is required.");
            if (dto.Name.Trim().Length > 120) throw new InvalidOperationException("Account name cannot exceed 120 characters.");
            if (!Enum.IsDefined(dto.Type)) throw new InvalidOperationException("Select a valid account type.");
            if (dto.LedgerCode?.Trim().Length > 30) throw new InvalidOperationException("Ledger code cannot exceed 30 characters.");
            if (string.IsNullOrWhiteSpace(dto.AccountHolderName)) throw new InvalidOperationException("Account holder name is required.");
            if (dto.AccountHolderName.Trim().Length > 150) throw new InvalidOperationException("Account holder name cannot exceed 150 characters.");
            if (Math.Abs(dto.OpeningBalance) > 999_999_999_999_999.99m) throw new InvalidOperationException("Opening balance is outside the supported range.");
            if (dto.BankOrWalletName?.Trim().Length > 150) throw new InvalidOperationException("Bank or wallet name cannot exceed 150 characters.");
            if (dto.Description?.Trim().Length > 1000) throw new InvalidOperationException("Description cannot exceed 1000 characters.");
        }

        private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

        private void ApplyConcurrencyToken(FinanceAccount account, string token)
        {
            if (string.IsNullOrWhiteSpace(token))
            {
                if (account.RowVersion.Length == 0) return;
                throw new InvalidOperationException("The account version is missing. Refresh and try again.");
            }
            try { _context.Entry(account).Property(a => a.RowVersion).OriginalValue = Convert.FromBase64String(token); }
            catch (FormatException) { throw new InvalidOperationException("The account version is invalid. Refresh and try again."); }
        }
    }
}

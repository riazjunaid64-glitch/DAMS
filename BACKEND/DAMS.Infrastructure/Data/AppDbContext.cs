using Microsoft.EntityFrameworkCore;
using DAMS.Domain.Entities;
using DAMS.Domain.Enums;

namespace DAMS.Infrastructure.Data
{
    public class AppDbContext : DbContext
    {
        public AppDbContext(DbContextOptions<AppDbContext> options)
            : base(options)
        {
        }

        public DbSet<User> Users { get; set; }
        public DbSet<Role> Roles { get; set; }
        public DbSet<Customer> Customers { get; set; }
        public DbSet<CustomerDocumentCategory> CustomerDocumentCategories { get; set; }
        public DbSet<CustomerDocumentRequirement> CustomerDocumentRequirements { get; set; }
        public DbSet<CustomerDocumentVersion> CustomerDocumentVersions { get; set; }
        public DbSet<CustomerDocumentAuditEntry> CustomerDocumentAuditEntries { get; set; }
        public DbSet<Project> Projects { get; set; }
        public DbSet<Unit> Units { get; set; }
        public DbSet<ProjectMedia> ProjectMedias { get; set; }
        public DbSet<UnitMedia> UnitMedias { get; set; }
        public DbSet<Booking> Bookings { get; set; }
        public DbSet<Installment> Installments { get; set; }
        public DbSet<Payment> Payments { get; set; }
        public DbSet<Employee> Employees { get; set; }
        public DbSet<EmployeeAttendance> EmployeeAttendances { get; set; }
        public DbSet<EmployeeTask> EmployeeTasks { get; set; }
        public DbSet<EmployeeSalary> EmployeeSalaries { get; set; }
        public DbSet<BookingRequest> BookingRequests { get; set; }
        public DbSet<Expense> Expenses { get; set; }
        public DbSet<AssetPurchase> AssetPurchases { get; set; }
        public DbSet<ExpenseCategory> ExpenseCategories { get; set; }
        public DbSet<Vendor> Vendors { get; set; }
        public DbSet<WhtDeposit> WhtDeposits { get; set; }
        public DbSet<FinanceSetting> FinanceSettings { get; set; }
        public DbSet<ManualRevenue> ManualRevenues { get; set; }
        public DbSet<RevenueCategory> RevenueCategories { get; set; }
        public DbSet<FinanceAttachment> FinanceAttachments { get; set; }
        public DbSet<FinanceAccount> FinanceAccounts { get; set; }
        public DbSet<OpeningBalanceSet> OpeningBalanceSets { get; set; }
        public DbSet<OpeningBalanceEntry> OpeningBalanceEntries { get; set; }
        public DbSet<OpeningBalanceAuditEntry> OpeningBalanceAuditEntries { get; set; }
        public DbSet<CapitalPartner> CapitalPartners { get; set; }
        public DbSet<CapitalTransaction> CapitalTransactions { get; set; }
        public DbSet<ThirdPartyPartner> ThirdPartyPartners { get; set; }
        public DbSet<ThirdPartyAttribution> ThirdPartyAttributions { get; set; }
        public DbSet<CommissionRule> CommissionRules { get; set; }
        public DbSet<CommissionRuleRevision> CommissionRuleRevisions { get; set; }
        public DbSet<BookingCommission> BookingCommissions { get; set; }
        public DbSet<CommissionPayout> CommissionPayouts { get; set; }
        public DbSet<CommissionPayoutReversal> CommissionPayoutReversals { get; set; }
        public DbSet<CustomerRebate> CustomerRebates { get; set; }
        public DbSet<RebateDisbursement> RebateDisbursements { get; set; }
        public DbSet<RebateDisbursementReversal> RebateDisbursementReversals { get; set; }
        public DbSet<FinancialEvidence> FinancialEvidence { get; set; }
        public DbSet<FinancialWorkflowAuditEntry> FinancialWorkflowAuditEntries { get; set; }
        public DbSet<Team> Teams { get; set; }
        public DbSet<LeadSource> LeadSources { get; set; }
        public DbSet<LeadClosureReason> LeadClosureReasons { get; set; }
        public DbSet<Lead> Leads { get; set; }
        public DbSet<LeadExternalSubmission> LeadExternalSubmissions { get; set; }
        public DbSet<LeadActivity> LeadActivities { get; set; }
        public DbSet<LeadAssignmentHistory> LeadAssignmentHistories { get; set; }
        public DbSet<LeadCommunication> LeadCommunications { get; set; }
        public DbSet<LeadFollowUp> LeadFollowUps { get; set; }
        public DbSet<LeadSiteVisit> LeadSiteVisits { get; set; }
        public DbSet<LeadDocument> LeadDocuments { get; set; }
        public DbSet<LeadComment> LeadComments { get; set; }
        public DbSet<LeadCommentMention> LeadCommentMentions { get; set; }

        // Central notification platform. Every module writes these; no module owns them.
        public DbSet<Notification> Notifications { get; set; }
        public DbSet<NotificationDelivery> NotificationDeliveries { get; set; }
        public DbSet<NotificationTemplate> NotificationTemplates { get; set; }
        public DbSet<NotificationRule> NotificationRules { get; set; }
        public DbSet<NotificationPreference> NotificationPreferences { get; set; }
        public DbSet<PushSubscription> PushSubscriptions { get; set; }
        public DbSet<NotificationSetting> NotificationSettings { get; set; }
        public DbSet<NotificationJob> NotificationJobs { get; set; }
        public DbSet<EmailSuppression> EmailSuppressions { get; set; }
        public DbSet<NotificationAuditEntry> NotificationAuditEntries { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            modelBuilder.Entity<Role>().HasData(
                new Role { RoleId = 1, Role_name = "Admin" },
                new Role { RoleId = 2, Role_name = "Client" },
                // Lead management introduces internal staff logins. Manager and Employee are
                // the two sales roles the lead workflow authorises against.
                new Role { RoleId = 3, Role_name = "Manager" },
                new Role { RoleId = 4, Role_name = "Employee" }
            );

            modelBuilder.Entity<Project>(entity =>
            {
                entity.HasIndex(p => p.ProjectName)
                      .IsUnique();

                entity.Property(p => p.ProjectName)
                      .IsRequired()
                      .HasMaxLength(200);

                entity.Property(p => p.Location)
                      .IsRequired()
                      .HasMaxLength(300);

                entity.Property(p => p.Description)
                      .HasMaxLength(1000);

                entity.Property(p => p.Category)
                      .HasMaxLength(50);
            });

            modelBuilder.Entity<Unit>(entity =>
            {
                entity.HasIndex(u => u.ProjectId);
                entity.HasIndex(u => new { u.ProjectId, u.FloorNumber, u.UnitNumber });

                entity.Property(u => u.UnitNumber)
                      .IsRequired()
                      .HasMaxLength(50);

                entity.Property(u => u.UnitType)
                      .IsRequired()
                      .HasMaxLength(100);

                entity.Property(u => u.Price)
                      .HasColumnType("decimal(18,2)");

                entity.Property(u => u.Size)
                    .HasColumnType("decimal(18,2)");

                // DB column is nvarchar (legacy migration); enum defaults to int in EF and caused InvalidCastException.
                entity.Property(u => u.Status)
                    .HasConversion(
                        v => v.ToString(),
                        v => Enum.Parse<UnitStatus>(v, true));

    entity.HasOne(u => u.Project)
          .WithMany(p => p.Units)
          .HasForeignKey(u => u.ProjectId)
          .OnDelete(DeleteBehavior.Restrict);
});



            modelBuilder.Entity<ProjectMedia>(entity =>
            {
                entity.Property(pm => pm.MediaUrl)
                      .IsRequired()
                      .HasMaxLength(500);

                entity.Property(pm => pm.MediaType)
                      .IsRequired()
                      .HasMaxLength(50);

                entity.Property(pm => pm.Category)
                      .HasConversion<int>();

                entity.Property(pm => pm.AltText)
                      .HasMaxLength(500);

                entity.Property(pm => pm.Description)
                      .HasMaxLength(1000);

                entity.Property(pm => pm.OriginalFileName)
                      .HasMaxLength(255);

                entity.Property(pm => pm.MimeType)
                      .HasMaxLength(100);

                entity.HasIndex(pm => new { pm.ProjectId, pm.IsCover });
                entity.HasIndex(pm => new { pm.ProjectId, pm.DisplayOrder });
                entity.HasIndex(pm => new { pm.ProjectId, pm.Category });

                entity.HasOne(pm => pm.Project)
                      .WithMany(p => p.MediaFiles)
                      .HasForeignKey(pm => pm.ProjectId)
                      .OnDelete(DeleteBehavior.Cascade);
            });

            modelBuilder.Entity<UnitMedia>(entity =>
            {
                entity.Property(um => um.MediaUrl)
                      .IsRequired()
                      .HasMaxLength(500);

                entity.Property(um => um.MediaType)
                      .IsRequired()
                      .HasMaxLength(50);

                entity.Property(um => um.Category)
                      .HasConversion<int>();

                entity.Property(um => um.AltText)
                      .HasMaxLength(500);

                entity.Property(um => um.Description)
                      .HasMaxLength(1000);

                entity.Property(um => um.OriginalFileName)
                      .HasMaxLength(255);

                entity.Property(um => um.MimeType)
                      .HasMaxLength(100);

                entity.HasIndex(um => new { um.UnitId, um.IsCover });
                entity.HasIndex(um => new { um.UnitId, um.DisplayOrder });
                entity.HasIndex(um => new { um.UnitId, um.Category });

                entity.HasOne(um => um.Unit)
                      .WithMany(u => u.MediaFiles)
                      .HasForeignKey(um => um.UnitId)
                      .OnDelete(DeleteBehavior.Cascade);
            });

            modelBuilder.Entity<Customer>(entity =>
            {
                entity.Property(c => c.FullName).IsRequired().HasMaxLength(200);
                entity.Property(c => c.FatherName).HasMaxLength(200);
                entity.Property(c => c.Phone).IsRequired().HasMaxLength(50);
                entity.Property(c => c.CNIC).HasMaxLength(50);
                entity.Property(c => c.Email).HasMaxLength(200);
                entity.Property(c => c.Address).HasMaxLength(500);
                entity.Property(c => c.SourceNotes).HasMaxLength(500);
                entity.Property(c => c.Notes).HasMaxLength(1000);
                entity.Property(c => c.Nationality).HasMaxLength(100);
                entity.Property(c => c.Occupation).HasMaxLength(150);
                entity.Property(c => c.Whatsapp).HasMaxLength(50);
                entity.Property(c => c.Source).HasConversion<int>();
                entity.Property(c => c.Status).HasConversion<int>();

                entity.HasIndex(c => c.Phone);
                entity.HasIndex(c => c.CNIC);
                entity.HasIndex(c => c.Email);
                entity.HasIndex(c => c.Status);
                // Customers list always orders by CreatedAt (newest first) — index the sort key.
                entity.HasIndex(c => c.CreatedAt);

                entity.HasOne(c => c.User)
                      .WithMany()
                      .HasForeignKey(c => c.UserId)
                      .OnDelete(DeleteBehavior.SetNull);
            });

            modelBuilder.Entity<CustomerDocumentCategory>(entity =>
            {
                entity.Property(c => c.Name).IsRequired().HasMaxLength(150);
                entity.Property(c => c.Code).IsRequired().HasMaxLength(80);
                entity.Property(c => c.Description).HasMaxLength(1000);
                entity.Property(c => c.AllowedFileTypes).IsRequired().HasMaxLength(100);
                entity.Property(c => c.CreatedByName).HasMaxLength(200);
                entity.Property(c => c.RowVersion).IsRowVersion();

                entity.HasIndex(c => c.Code).IsUnique();
                entity.HasIndex(c => new { c.IsActive, c.DisplayOrder });
                entity.HasIndex(c => c.AssignToNewCustomers);

                var seededAt = new DateTime(2026, 8, 4, 0, 0, 0, DateTimeKind.Utc);
                entity.HasData(
                    SeedDocumentCategory(1, "CNIC Front", "cnic_front", true, 10, seededAt),
                    SeedDocumentCategory(2, "CNIC Back", "cnic_back", true, 20, seededAt),
                    SeedDocumentCategory(3, "Customer Photograph", "customer_photo", true, 30, seededAt),
                    SeedDocumentCategory(4, "Proof of Address", "proof_of_address", false, 40, seededAt),
                    SeedDocumentCategory(5, "Passport", "passport", false, 50, seededAt),
                    SeedDocumentCategory(6, "Next-of-Kin CNIC", "next_of_kin_cnic", false, 60, seededAt),
                    SeedDocumentCategory(7, "Signature Specimen", "signature_specimen", false, 70, seededAt),
                    SeedDocumentCategory(8, "Tax Document", "tax_document", false, 80, seededAt),
                    SeedDocumentCategory(9, "Other", "other", false, 90, seededAt));
            });

            modelBuilder.Entity<CustomerDocumentRequirement>(entity =>
            {
                entity.Property(r => r.Name).IsRequired().HasMaxLength(150);
                entity.Property(r => r.Description).HasMaxLength(1000);
                entity.Property(r => r.AllowedFileTypes).IsRequired().HasMaxLength(100);
                entity.Property(r => r.Status).HasConversion<int>();
                entity.Property(r => r.LastActionByName).HasMaxLength(200);
                entity.Property(r => r.RowVersion).IsRowVersion();

                entity.HasIndex(r => new { r.CustomerId, r.CategoryId })
                      .IsUnique()
                      .HasFilter("[CategoryId] IS NOT NULL");
                entity.HasIndex(r => new { r.CustomerId, r.Name }, "IX_CustomerDocumentRequirements_CustomerId_CustomName")
                      .IsUnique()
                      .HasFilter("[CategoryId] IS NULL");
                entity.HasIndex(r => new { r.CustomerId, r.Status });
                entity.HasIndex(r => new { r.CustomerId, r.DisplayOrder });
                entity.HasIndex(r => r.CategoryId);

                entity.HasOne(r => r.Customer)
                      .WithMany(c => c.DocumentRequirements)
                      .HasForeignKey(r => r.CustomerId)
                      .OnDelete(DeleteBehavior.Restrict);
                entity.HasOne(r => r.Category)
                      .WithMany(c => c.Requirements)
                      .HasForeignKey(r => r.CategoryId)
                      .OnDelete(DeleteBehavior.Restrict);
            });

            modelBuilder.Entity<CustomerDocumentVersion>(entity =>
            {
                entity.Property(v => v.StoredFileName).IsRequired().HasMaxLength(100);
                entity.Property(v => v.OriginalFileName).IsRequired().HasMaxLength(255);
                entity.Property(v => v.ContentType).IsRequired().HasMaxLength(100);
                entity.Property(v => v.UploadedByName).HasMaxLength(200);
                entity.Property(v => v.ReviewedByName).HasMaxLength(200);
                entity.Property(v => v.ReviewReason).HasMaxLength(2000);
                entity.Property(v => v.ReviewStatus).HasConversion<int>();
                entity.Property(v => v.RowVersion).IsRowVersion();

                entity.HasIndex(v => new { v.RequirementId, v.VersionNumber }).IsUnique();
                entity.HasIndex(v => v.RequirementId, "IX_CustomerDocumentVersions_RequirementId_Current")
                      .IsUnique()
                      .HasFilter("[IsCurrent] = 1");
                entity.HasIndex(v => v.UploadedAt);

                entity.HasOne(v => v.Requirement)
                      .WithMany(r => r.Versions)
                      .HasForeignKey(v => v.RequirementId)
                      .OnDelete(DeleteBehavior.Restrict);
            });

            modelBuilder.Entity<CustomerDocumentAuditEntry>(entity =>
            {
                entity.Property(a => a.Action).HasConversion<int>();
                entity.Property(a => a.PreviousStatus).HasConversion<int?>();
                entity.Property(a => a.NewStatus).HasConversion<int?>();
                // Unbounded: category change summaries record exact before/after values for every
                // control field; a fixed cap would silently truncate and lose the audited values.
                entity.Property(a => a.Notes);
                entity.Property(a => a.PerformedByName).HasMaxLength(200);

                entity.HasIndex(a => new { a.CustomerId, a.OccurredAt });
                entity.HasIndex(a => new { a.RequirementId, a.OccurredAt });
                entity.HasIndex(a => a.CategoryId);

                entity.HasOne(a => a.Customer)
                      .WithMany()
                      .HasForeignKey(a => a.CustomerId)
                      .OnDelete(DeleteBehavior.Restrict);
                entity.HasOne(a => a.Requirement)
                      .WithMany(r => r.AuditEntries)
                      .HasForeignKey(a => a.RequirementId)
                      .OnDelete(DeleteBehavior.Restrict);
                entity.HasOne(a => a.Category)
                      .WithMany()
                      .HasForeignKey(a => a.CategoryId)
                      .OnDelete(DeleteBehavior.SetNull);
                entity.HasOne(a => a.Version)
                      .WithMany()
                      .HasForeignKey(a => a.VersionId)
                      .OnDelete(DeleteBehavior.SetNull);
            });

            modelBuilder.Entity<Booking>(entity =>
            {
                entity.Property(b => b.BookingReference).IsRequired().HasMaxLength(50);
                entity.Property(b => b.DiscountReason).HasMaxLength(500);
                entity.Property(b => b.CustomerNotes).HasMaxLength(1000);
                entity.Property(b => b.InternalNotes).HasMaxLength(1000);

                entity.Property(b => b.ListPrice).HasColumnType("decimal(18,2)");
                entity.Property(b => b.AgreedSalePrice).HasColumnType("decimal(18,2)");
                entity.Property(b => b.DiscountAmount).HasColumnType("decimal(18,2)");
                entity.Property(b => b.BookingAmountRequired).HasColumnType("decimal(18,2)");
                entity.Property(b => b.BookingAmountReceived).HasColumnType("decimal(18,2)");
                entity.Property(b => b.TotalInstallmentAmount).HasColumnType("decimal(18,2)");
                entity.Property(b => b.PossessionAmount).HasColumnType("decimal(18,2)");
                entity.Property(b => b.PricePerSft).HasColumnType("decimal(18,2)");
                entity.Property(b => b.DiscountPercent).HasColumnType("decimal(5,2)");
                entity.Property(b => b.ApplicationAmountReceived).HasColumnType("decimal(18,2)");
                entity.Property(b => b.SerialNo).HasMaxLength(50);
                entity.Property(b => b.ApartmentCategory).HasMaxLength(100);
                entity.Property(b => b.Tower).HasMaxLength(50);
                entity.Property(b => b.ReferenceId).HasMaxLength(100);
                entity.Property(b => b.PaymentThrough).HasMaxLength(200);
                entity.Property(b => b.ApplicationPaymentType).HasMaxLength(30);
                entity.Property(b => b.NextOfKinName).HasMaxLength(200);
                entity.Property(b => b.NextOfKinRelation).HasMaxLength(100);
                entity.Property(b => b.NextOfKinContact).HasMaxLength(50);
                entity.Property(b => b.NextOfKinCnic).HasMaxLength(50);
                entity.Property(b => b.NextOfKinAddress).HasMaxLength(500);
                entity.Property(b => b.Source).HasConversion<int>();
                entity.Property(b => b.Status).HasConversion<int>();
                entity.Property(b => b.InstallmentFrequency).HasConversion<int>();
                entity.Property(b => b.RowVersion).IsRowVersion();

                entity.HasIndex(b => b.BookingReference).IsUnique();
                entity.HasIndex(b => b.CustomerId);
                entity.HasIndex(b => b.UnitId);
                // At most one non-cancelled booking per unit, enforced by the database so
                // two concurrent creates cannot both pass the app-level check. The named
                // overload keeps this separate from the plain UnitId index above.
                entity.HasIndex(b => b.UnitId, "IX_Bookings_UnitId_Active")
                      .IsUnique()
                      .HasFilter($"[Status] <> {(int)BookingStatus.Cancelled}");
                entity.HasIndex(b => b.Status);
                // Bookings list pages order by BookingDate (often within a Status filter); these
                // indexes let SQL Server serve the sorted page from the index instead of sorting.
                entity.HasIndex(b => b.BookingDate);
                entity.HasIndex(b => new { b.Status, b.BookingDate });

                entity.HasOne(b => b.Customer)
                      .WithMany(c => c.Bookings)
                      .HasForeignKey(b => b.CustomerId)
                      .OnDelete(DeleteBehavior.Restrict);
                entity.HasOne(b => b.Unit)
                      .WithMany(u => u.Bookings)
                      .HasForeignKey(b => b.UnitId)
                      .OnDelete(DeleteBehavior.Restrict);
                entity.HasOne(b => b.BookingRequest)
                      .WithMany()
                      .HasForeignKey(b => b.BookingRequestId)
                      .OnDelete(DeleteBehavior.SetNull);
            });

            modelBuilder.Entity<Installment>(entity =>
            {
                entity.Property(i => i.Amount).HasColumnType("decimal(18,2)");
                entity.Property(i => i.Status).HasConversion<int>();
                entity.Property(i => i.Type).HasConversion<int>();
                entity.Property(i => i.Notes).HasMaxLength(1000);
                entity.HasIndex(i => new { i.BookingId, i.SequenceNumber }).IsUnique();
                entity.HasIndex(i => new { i.Status, i.DueDate });
                entity.HasOne(i => i.Booking)
                      .WithMany(b => b.Installments)
                      .HasForeignKey(i => i.BookingId)
                      .OnDelete(DeleteBehavior.Cascade);
            });

            modelBuilder.Entity<Payment>(entity =>
            {
                entity.ToTable("Payments");
                entity.Property(p => p.Amount).HasColumnType("decimal(18,2)");
                entity.Property(p => p.PaymentMethod).HasConversion<int>();
                entity.Property(p => p.Type).HasConversion<int>();
                entity.Property(p => p.PaymentReference).HasMaxLength(500);
                entity.Property(p => p.ReceiptNumber).HasMaxLength(20);
                entity.Property(p => p.Notes).HasMaxLength(1000);
                entity.HasIndex(p => p.ReceiptNumber)
                      .IsUnique()
                      .HasFilter("[ReceiptNumber] IS NOT NULL");
                entity.HasIndex(p => p.BookingId);
                entity.HasIndex(p => p.InstallmentId);
                entity.HasIndex(p => new { p.BookingId, p.Type });
                entity.HasIndex(p => p.PaidAt);
                entity.HasIndex(p => p.FinanceAccountId);
                entity.HasOne(p => p.Booking)
                      .WithMany(b => b.Payments)
                      .HasForeignKey(p => p.BookingId)
                      .OnDelete(DeleteBehavior.Cascade);
                entity.HasOne(p => p.Installment)
                      .WithMany(i => i.Payments)
                      .HasForeignKey(p => p.InstallmentId)
                      .OnDelete(DeleteBehavior.Restrict);
                // Restrict, matching every other account link: an account that has received
                // money cannot be deleted out from under the balance it explains.
                entity.HasOne(p => p.FinanceAccount)
                      .WithMany(a => a.Payments)
                      .HasForeignKey(p => p.FinanceAccountId)
                      .OnDelete(DeleteBehavior.Restrict);
            });

            modelBuilder.Entity<Employee>(entity =>
            {
                entity.Property(e => e.FullName).IsRequired().HasMaxLength(200);
                entity.Property(e => e.JobTitle).IsRequired().HasMaxLength(100);
                entity.Property(e => e.Department).IsRequired().HasMaxLength(100);
                entity.Property(e => e.Phone).IsRequired().HasMaxLength(50);
                entity.Property(e => e.Email).HasMaxLength(200);
                entity.Property(e => e.Address).HasMaxLength(500);
                entity.Property(e => e.Salary).HasColumnType("decimal(18,2)");
                entity.Property(e => e.Status).HasConversion<int>();

                // One login maps to at most one employee record, so resolving "who am I"
                // from a token is unambiguous.
                entity.HasIndex(e => e.UserId)
                      .IsUnique()
                      .HasFilter("[UserId] IS NOT NULL");
                entity.HasIndex(e => e.TeamId);

                entity.HasOne(e => e.User)
                      .WithMany()
                      .HasForeignKey(e => e.UserId)
                      .OnDelete(DeleteBehavior.SetNull);

                entity.HasOne(e => e.Team)
                      .WithMany(t => t.Members)
                      .HasForeignKey(e => e.TeamId)
                      .OnDelete(DeleteBehavior.SetNull);
            });

            modelBuilder.Entity<Team>(entity =>
            {
                entity.Property(t => t.Name).IsRequired().HasMaxLength(150);
                entity.HasIndex(t => t.Name).IsUnique();

                entity.HasOne(t => t.ManagerEmployee)
                      .WithMany()
                      .HasForeignKey(t => t.ManagerEmployeeId)
                      .OnDelete(DeleteBehavior.NoAction);
            });

            modelBuilder.Entity<EmployeeAttendance>(entity =>
            {
                entity.Property(a => a.Status).HasConversion<int>();
                entity.Property(a => a.Notes).HasMaxLength(500);
                entity.HasIndex(a => new { a.EmployeeId, a.Date }).IsUnique();
                entity.HasOne(a => a.Employee)
                      .WithMany(e => e.Attendances)
                      .HasForeignKey(a => a.EmployeeId)
                      .OnDelete(DeleteBehavior.Cascade);
            });

            modelBuilder.Entity<EmployeeTask>(entity =>
            {
                entity.Property(t => t.Title).IsRequired().HasMaxLength(200);
                entity.Property(t => t.Description).HasMaxLength(1000);
                entity.Property(t => t.Priority).HasConversion<int>();
                entity.Property(t => t.Status).HasConversion<int>();
                entity.HasIndex(t => t.EmployeeId);
                entity.HasIndex(t => t.ProjectId);
                entity.HasOne(t => t.Employee)
                      .WithMany(e => e.Tasks)
                      .HasForeignKey(t => t.EmployeeId)
                      .OnDelete(DeleteBehavior.Cascade);
                entity.HasOne(t => t.Project)
                      .WithMany()
                      .HasForeignKey(t => t.ProjectId)
                      .OnDelete(DeleteBehavior.SetNull);
            });

            modelBuilder.Entity<EmployeeSalary>(entity =>
            {
                entity.Property(s => s.Amount).HasColumnType("decimal(18,2)");
                entity.Property(s => s.ProjectName).HasMaxLength(200);
                entity.Property(s => s.Notes).HasMaxLength(500);
                entity.HasIndex(s => s.EmployeeId);
                // Unique so a double-click cannot record the same month twice.
                entity.HasIndex(s => new { s.EmployeeId, s.PayYear, s.PayMonth }).IsUnique();
                entity.HasOne(s => s.Employee)
                      .WithMany(e => e.Salaries)
                      .HasForeignKey(s => s.EmployeeId)
                      .OnDelete(DeleteBehavior.Cascade);
                entity.HasOne(s => s.Project)
                      .WithMany()
                      .HasForeignKey(s => s.ProjectId)
                      .OnDelete(DeleteBehavior.SetNull);
                entity.HasOne(s => s.Expense)
                      .WithMany()
                      .HasForeignKey(s => s.ExpenseId)
                      .OnDelete(DeleteBehavior.SetNull);
            });

            modelBuilder.Entity<BookingRequest>(entity =>
            {
                entity.Property(br => br.FullName).IsRequired().HasMaxLength(200);
                entity.Property(br => br.Phone).IsRequired().HasMaxLength(50);
                entity.Property(br => br.Email).IsRequired().HasMaxLength(200);
                entity.Property(br => br.CNIC).IsRequired().HasMaxLength(50);
                entity.Property(br => br.Address).IsRequired().HasMaxLength(500);
                entity.Property(br => br.Notes).HasMaxLength(1000);
                entity.Property(br => br.RejectionReason).HasMaxLength(500);
                entity.Property(br => br.Status).HasConversion<int>();

                entity.HasIndex(br => br.UnitId);
                entity.HasIndex(br => br.UserId);
                entity.HasIndex(br => br.Status);
                entity.HasIndex(br => br.RequestedAt);
                entity.HasIndex(br => new { br.Status, br.RequestedAt });
                entity.HasIndex(br => new { br.UserId, br.RequestedAt });
                // A known lead may submit several website enquiries over time. Each request
                // points at one lead; many requests may point at the same lead.
                entity.HasIndex(br => br.LeadId);

                entity.HasOne(br => br.Lead)
                      .WithMany()
                      .HasForeignKey(br => br.LeadId)
                      .OnDelete(DeleteBehavior.NoAction);

                entity.HasOne(br => br.Unit)
                      .WithMany()
                      .HasForeignKey(br => br.UnitId)
                      .OnDelete(DeleteBehavior.Restrict);

                entity.HasOne(br => br.User)
                      .WithMany()
                      .HasForeignKey(br => br.UserId)
                      .OnDelete(DeleteBehavior.NoAction);

                entity.HasOne(br => br.ReviewedBy)
                      .WithMany()
                      .HasForeignKey(br => br.ReviewedByUserId)
                      .OnDelete(DeleteBehavior.NoAction);

                entity.HasOne(br => br.Customer)
                      .WithMany()
                      .HasForeignKey(br => br.CustomerId)
                      .OnDelete(DeleteBehavior.NoAction);
            });

            modelBuilder.Entity<Expense>(entity =>
            {
                entity.Property(e => e.Amount).HasColumnType("decimal(18,2)");
                entity.Property(e => e.Category).IsRequired().HasMaxLength(100);
                entity.Property(e => e.Description).HasMaxLength(1000);
                entity.Property(e => e.Vendor).HasMaxLength(200);

                // Rates are stored to 4 decimals: a rate back-computed from a hand-entered tax
                // amount (e.g. 4.7619%) must round-trip, or the s.165 statement stops tying out.
                entity.Property(e => e.WhtRate).HasColumnType("decimal(9,4)");
                entity.Property(e => e.WhtAmount).HasColumnType("decimal(18,2)");
                entity.Property(e => e.WhtOverrideReason).HasMaxLength(500);
                entity.Property(e => e.WhtTaxSection).HasMaxLength(30);
                entity.Property(e => e.VendorFilerStatusAtEntry).HasConversion<int>();
                // Derived from Amount and WhtAmount; storing it would let the three drift apart.
                entity.Ignore(e => e.NetPaid);

                entity.HasIndex(e => e.ProjectId);
                entity.HasIndex(e => e.Date);
                entity.HasIndex(e => e.Category);
                entity.HasIndex(e => e.FinanceAccountId);
                entity.HasIndex(e => e.CategoryId);
                // Threshold checks ask "what has this vendor been paid this year"; the WHT reports
                // scan the same rows by date. Both are covered here.
                entity.HasIndex(e => new { e.VendorId, e.Date });
                entity.HasIndex(e => new { e.WhtApplied, e.Date })
                      .HasFilter("[WhtApplied] = 1");

                entity.HasOne(e => e.Project)
                      .WithMany()
                      .HasForeignKey(e => e.ProjectId)
                      .OnDelete(DeleteBehavior.SetNull);

                entity.HasOne(e => e.FinanceAccount)
                      .WithMany(a => a.Expenses)
                      .HasForeignKey(e => e.FinanceAccountId)
                      .OnDelete(DeleteBehavior.Restrict);

                // Restrict, not SetNull: severing the link would leave the snapshot columns
                // (Category, WhtRate, WhtTaxSection) as the only record of what was withheld.
                entity.HasOne(e => e.ExpenseCategory)
                      .WithMany(c => c.Expenses)
                      .HasForeignKey(e => e.CategoryId)
                      .OnDelete(DeleteBehavior.Restrict);

                entity.HasOne(e => e.VendorAccount)
                      .WithMany(v => v.Expenses)
                      .HasForeignKey(e => e.VendorId)
                      .OnDelete(DeleteBehavior.Restrict);
            });

            modelBuilder.Entity<AssetPurchase>(entity =>
            {
                entity.Property(p => p.Amount).HasColumnType("decimal(18,2)");
                entity.Property(p => p.ItemName).IsRequired().HasMaxLength(200);
                entity.Property(p => p.Category).IsRequired().HasMaxLength(100);
                entity.Property(p => p.Description).HasMaxLength(1000);
                entity.Property(p => p.Vendor).HasMaxLength(200);

                entity.Property(p => p.WhtRate).HasColumnType("decimal(9,4)");
                entity.Property(p => p.WhtAmount).HasColumnType("decimal(18,2)");
                entity.Property(p => p.WhtOverrideReason).HasMaxLength(500);
                entity.Property(p => p.WhtTaxSection).HasMaxLength(30);
                entity.Property(p => p.VendorFilerStatusAtEntry).HasConversion<int>();
                entity.Ignore(p => p.NetPaid);

                entity.HasIndex(p => p.ProjectId);
                entity.HasIndex(p => p.Date);
                entity.HasIndex(p => p.FinanceAccountId);
                // The asset ledger's own drill-down and running total read by this.
                entity.HasIndex(p => new { p.AssetAccountId, p.Date });
                entity.HasIndex(p => p.CategoryId);
                entity.HasIndex(p => new { p.VendorId, p.Date });

                entity.HasOne(p => p.Project)
                      .WithMany()
                      .HasForeignKey(p => p.ProjectId)
                      .OnDelete(DeleteBehavior.SetNull);

                // Two accounts, both Restrict. Deleting either side would leave a purchase that
                // debits an asset without crediting cash, or vice versa — a silently unbalanced
                // balance sheet. FinanceAccountService refuses the delete before it gets here.
                entity.HasOne(p => p.AssetAccount)
                      .WithMany(a => a.AssetPurchasesReceived)
                      .HasForeignKey(p => p.AssetAccountId)
                      .OnDelete(DeleteBehavior.Restrict);

                entity.HasOne(p => p.FinanceAccount)
                      .WithMany(a => a.AssetPurchasesPaid)
                      .HasForeignKey(p => p.FinanceAccountId)
                      .OnDelete(DeleteBehavior.Restrict);

                entity.HasOne(p => p.ExpenseCategory)
                      .WithMany(c => c.AssetPurchases)
                      .HasForeignKey(p => p.CategoryId)
                      .OnDelete(DeleteBehavior.Restrict);

                entity.HasOne(p => p.VendorAccount)
                      .WithMany(v => v.AssetPurchases)
                      .HasForeignKey(p => p.VendorId)
                      .OnDelete(DeleteBehavior.Restrict);
            });

            ConfigureWithholdingTax(modelBuilder);

            modelBuilder.Entity<ManualRevenue>(entity =>
            {
                entity.Property(r => r.Amount).HasColumnType("decimal(18,2)");
                entity.Property(r => r.RevenueType).IsRequired().HasMaxLength(100);
                entity.Property(r => r.RevenueTypeName).IsRequired().HasMaxLength(150);
                entity.Property(r => r.Description).HasMaxLength(1000);
                entity.Property(r => r.Reference).HasMaxLength(200);

                entity.HasIndex(r => r.ProjectId);
                entity.HasIndex(r => r.Date);
                entity.HasIndex(r => r.RevenueType);
                entity.HasIndex(r => r.FinanceAccountId);
                entity.HasIndex(r => r.RevenueCategoryId);

                entity.HasOne(r => r.Project)
                      .WithMany()
                      .HasForeignKey(r => r.ProjectId)
                      .OnDelete(DeleteBehavior.SetNull);

                entity.HasOne(r => r.FinanceAccount)
                      .WithMany(a => a.ManualRevenues)
                      .HasForeignKey(r => r.FinanceAccountId)
                      .OnDelete(DeleteBehavior.Restrict);

                entity.HasOne(r => r.RevenueCategory)
                      .WithMany(c => c.ManualRevenues)
                      .HasForeignKey(r => r.RevenueCategoryId)
                      .OnDelete(DeleteBehavior.Restrict);
            });

            ConfigureFinanceReporting(modelBuilder);

            modelBuilder.Entity<FinanceAccount>(entity =>
            {
                entity.Property(a => a.Name).IsRequired().HasMaxLength(120);
                entity.Property(a => a.AccountHolderName).IsRequired().HasMaxLength(150);
                entity.Property(a => a.BankOrWalletName).HasMaxLength(150);
                entity.Property(a => a.Description).HasMaxLength(1000);
                entity.Property(a => a.OpeningBalance).HasColumnType("decimal(18,2)");
                entity.Property(a => a.LedgerCode).HasMaxLength(30);
                entity.Property(a => a.SystemRole).HasConversion<int>();
                entity.Property(a => a.RowVersion).IsRowVersion();

                entity.HasIndex(a => a.Name).IsUnique();
                entity.HasIndex(a => new { a.IsActive, a.Type });
                entity.HasIndex(a => a.AccountHolderName);
                entity.HasIndex(a => new { a.Type, a.DisplayOrder });
                entity.HasIndex(a => a.LedgerCode);
                entity.HasIndex(a => a.SystemRole)
                      .IsUnique()
                      .HasFilter("[SystemRole] <> 0");

                entity.ToTable(t =>
                {
                    t.HasCheckConstraint("CK_FinanceAccounts_SystemRole", "[SystemRole] >= 0 AND [SystemRole] <= 1");
                    t.HasCheckConstraint("CK_FinanceAccounts_TaxPayableRole", "[SystemRole] <> 1 OR [Type] = 5");
                });
            });

            modelBuilder.Entity<FinanceAttachment>(entity =>
            {
                entity.Property(a => a.StoredFileName).IsRequired().HasMaxLength(100);
                entity.Property(a => a.OriginalFileName).IsRequired().HasMaxLength(180);
                entity.Property(a => a.ContentType).IsRequired().HasMaxLength(150);

                entity.HasIndex(a => a.ManualRevenueId)
                      .IsUnique()
                      .HasFilter("[ManualRevenueId] IS NOT NULL");
                entity.HasIndex(a => a.ExpenseId)
                      .IsUnique()
                      .HasFilter("[ExpenseId] IS NOT NULL");
                entity.HasIndex(a => a.AssetPurchaseId)
                      .IsUnique()
                      .HasFilter("[AssetPurchaseId] IS NOT NULL");

                // Counted rather than enumerated as pairs: with three owners the pairwise form
                // needs six clauses and gains one more every time a record type is added.
                entity.ToTable(t => t.HasCheckConstraint(
                    "CK_FinanceAttachments_ExactlyOneOwner",
                    "(CASE WHEN [ManualRevenueId] IS NULL THEN 0 ELSE 1 END"
                    + " + CASE WHEN [ExpenseId] IS NULL THEN 0 ELSE 1 END"
                    + " + CASE WHEN [AssetPurchaseId] IS NULL THEN 0 ELSE 1 END) = 1"));

                entity.HasOne(a => a.ManualRevenue)
                      .WithOne(r => r.Attachment)
                      .HasForeignKey<FinanceAttachment>(a => a.ManualRevenueId)
                      .OnDelete(DeleteBehavior.Cascade);

                entity.HasOne(a => a.Expense)
                      .WithOne(e => e.Attachment)
                      .HasForeignKey<FinanceAttachment>(a => a.ExpenseId)
                      .OnDelete(DeleteBehavior.Cascade);

                entity.HasOne(a => a.AssetPurchase)
                      .WithOne(p => p.Attachment)
                      .HasForeignKey<FinanceAttachment>(a => a.AssetPurchaseId)
                      .OnDelete(DeleteBehavior.Cascade);
            });

            ConfigureCommissionAndRebates(modelBuilder);
            ConfigureLeadManagement(modelBuilder);
            ConfigureNotifications(modelBuilder);
        }

        private static void ConfigureFinanceReporting(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<RevenueCategory>(entity =>
            {
                entity.Property(c => c.Name).IsRequired().HasMaxLength(150);
                entity.Property(c => c.Code).IsRequired().HasMaxLength(80);
                entity.Property(c => c.Description).HasMaxLength(1000);
                entity.Property(c => c.RowVersion).IsRowVersion();
                entity.HasIndex(c => c.Name).IsUnique();
                entity.HasIndex(c => c.Code).IsUnique();
                entity.HasIndex(c => new { c.IsActive, c.DisplayOrder });
                entity.HasData(SeedRevenueCategories());
            });

            modelBuilder.Entity<OpeningBalanceSet>(entity =>
            {
                entity.Property(s => s.RowVersion).IsRowVersion();
                entity.HasIndex(s => s.AsAtDate).IsUnique();
                entity.ToTable(t => t.HasCheckConstraint("CK_OpeningBalanceSets_Singleton", "[Id] = 1"));
            });

            modelBuilder.Entity<OpeningBalanceEntry>(entity =>
            {
                entity.Property(e => e.DebitAmount).HasColumnType("decimal(18,2)");
                entity.Property(e => e.CreditAmount).HasColumnType("decimal(18,2)");
                entity.Property(e => e.Note).HasMaxLength(500);
                entity.HasIndex(e => new { e.OpeningBalanceSetId, e.FinanceAccountId }).IsUnique();
                entity.ToTable(t =>
                {
                    t.HasCheckConstraint("CK_OpeningBalanceEntry_NonNegative", "[DebitAmount] >= 0 AND [CreditAmount] >= 0");
                    t.HasCheckConstraint("CK_OpeningBalanceEntry_OneSide", "[DebitAmount] = 0 OR [CreditAmount] = 0");
                });
                entity.HasOne(e => e.OpeningBalanceSet).WithMany(s => s.Entries)
                    .HasForeignKey(e => e.OpeningBalanceSetId).OnDelete(DeleteBehavior.Cascade);
                entity.HasOne(e => e.FinanceAccount).WithMany(a => a.OpeningBalanceEntries)
                    .HasForeignKey(e => e.FinanceAccountId).OnDelete(DeleteBehavior.Restrict);
            });

            modelBuilder.Entity<OpeningBalanceAuditEntry>(entity =>
            {
                entity.Property(a => a.Action).IsRequired().HasMaxLength(30);
                entity.Property(a => a.Note).HasMaxLength(1000);
                entity.HasIndex(a => new { a.OpeningBalanceSetId, a.OccurredAt });
                entity.HasOne(a => a.OpeningBalanceSet).WithMany(s => s.AuditEntries)
                    .HasForeignKey(a => a.OpeningBalanceSetId).OnDelete(DeleteBehavior.Cascade);
            });

            modelBuilder.Entity<CapitalPartner>(entity =>
            {
                entity.Property(p => p.Name).IsRequired().HasMaxLength(200);
                entity.Property(p => p.Cnic).HasMaxLength(20);
                entity.Property(p => p.Ntn).HasMaxLength(30);
                entity.Property(p => p.ProfitSharePercent).HasColumnType("decimal(9,4)");
                entity.Property(p => p.RowVersion).IsRowVersion();
                entity.ToTable(t => t.HasCheckConstraint("CK_CapitalPartners_Share",
                    "[ProfitSharePercent] >= 0 AND [ProfitSharePercent] <= 100"));
                entity.HasIndex(p => p.Name).IsUnique();
                entity.HasIndex(p => p.FinanceAccountId).IsUnique().HasFilter("[FinanceAccountId] IS NOT NULL");
                entity.HasOne(p => p.FinanceAccount).WithMany(a => a.CapitalPartners)
                    .HasForeignKey(p => p.FinanceAccountId).OnDelete(DeleteBehavior.Restrict);
            });

            modelBuilder.Entity<CapitalTransaction>(entity =>
            {
                entity.Property(t => t.Type).HasConversion<int>();
                entity.Property(t => t.Amount).HasColumnType("decimal(18,2)");
                entity.Property(t => t.ProfitSharePercentSnapshot).HasColumnType("decimal(9,4)");
                entity.Property(t => t.Reference).HasMaxLength(200);
                entity.Property(t => t.Note).HasMaxLength(1000);
                entity.ToTable(t =>
                {
                    t.HasCheckConstraint("CK_CapitalTransactions_Amount", "[Amount] > 0");
                    t.HasCheckConstraint("CK_CapitalTransactions_Type", "[Type] >= 1 AND [Type] <= 5");
                    t.HasCheckConstraint("CK_CapitalTransactions_CashSide",
                        "([Type] IN (2, 3) AND [FinanceAccountId] IS NOT NULL) OR ([Type] NOT IN (2, 3) AND [FinanceAccountId] IS NULL)");
                    t.HasCheckConstraint("CK_CapitalTransactions_ProfitSnapshot",
                        "([Type] = 4 AND [ProfitSharePercentSnapshot] IS NOT NULL AND [ProfitSharePercentSnapshot] >= 0 AND [ProfitSharePercentSnapshot] <= 100) OR ([Type] <> 4 AND [ProfitSharePercentSnapshot] IS NULL)");
                });
                entity.HasIndex(t => new { t.CapitalPartnerId, t.Date });
                entity.HasIndex(t => t.FinanceAccountId);
                entity.HasOne(t => t.CapitalPartner).WithMany(p => p.Transactions)
                    .HasForeignKey(t => t.CapitalPartnerId).OnDelete(DeleteBehavior.Restrict);
                entity.HasOne(t => t.FinanceAccount).WithMany(a => a.CapitalCashTransactions)
                    .HasForeignKey(t => t.FinanceAccountId).OnDelete(DeleteBehavior.Restrict);
            });
        }

        private static RevenueCategory[] SeedRevenueCategories()
        {
            var names = new[]
            {
                "Transfer Charges", "Development Charges", "Possession Charges", "Membership Charges",
                "Documentation Charges", "NOC / NDC Charges", "Utility Connection Charges", "Parking Charges",
                "Late Payment Surcharge", "Cancellation / Forfeiture", "Rental Income", "Commission Income",
                "Bank Profit / Interest", "Other Income"
            };
            return names.Select((name, index) => new RevenueCategory
            {
                Id = index + 1,
                Name = name,
                Code = new string(name.ToLowerInvariant().Select(c => char.IsLetterOrDigit(c) ? c : '_').ToArray()).Replace("___", "_").Replace("__", "_").Trim('_'),
                DisplayOrder = (index + 1) * 10,
                IsActive = true,
                CreatedAt = new DateTime(2026, 8, 12, 0, 0, 0, DateTimeKind.Utc)
            }).ToArray();
        }

        private static void ConfigureCommissionAndRebates(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<ThirdPartyPartner>(entity =>
            {
                entity.Property(p => p.Name).IsRequired().HasMaxLength(200);
                entity.Property(p => p.PartnerType).IsRequired().HasMaxLength(80);
                entity.Property(p => p.ContactPerson).HasMaxLength(200);
                entity.Property(p => p.Phone).HasMaxLength(50);
                entity.Property(p => p.NormalizedPhone).HasMaxLength(50);
                entity.Property(p => p.Email).HasMaxLength(200);
                entity.Property(p => p.NormalizedEmail).HasMaxLength(200);
                entity.Property(p => p.Address).HasMaxLength(500);
                entity.Property(p => p.Cnic).HasMaxLength(50);
                entity.Property(p => p.NormalizedCnic).HasMaxLength(50);
                entity.Property(p => p.Ntn).HasMaxLength(80);
                entity.Property(p => p.NormalizedNtn).HasMaxLength(80);
                entity.Property(p => p.RegistrationNumber).HasMaxLength(100);
                entity.Property(p => p.InternalCode).IsRequired().HasMaxLength(80);
                entity.Property(p => p.BankName).HasMaxLength(150);
                entity.Property(p => p.AccountTitle).HasMaxLength(150);
                entity.Property(p => p.AccountNumber).HasMaxLength(100);
                entity.Property(p => p.Iban).HasMaxLength(100);
                entity.Property(p => p.Notes).HasMaxLength(2000);
                entity.Property(p => p.CreatedByName).HasMaxLength(200);
                entity.Property(p => p.RowVersion).IsRowVersion();
                entity.HasIndex(p => p.InternalCode).IsUnique();
                entity.HasIndex(p => p.NormalizedCnic).IsUnique().HasFilter("[NormalizedCnic] IS NOT NULL");
                entity.HasIndex(p => p.NormalizedNtn).IsUnique().HasFilter("[NormalizedNtn] IS NOT NULL");
                entity.HasIndex(p => p.NormalizedPhone);
                entity.HasIndex(p => p.NormalizedEmail);
                entity.HasIndex(p => new { p.IsActive, p.PartnerType });
            });

            modelBuilder.Entity<ThirdPartyAttribution>(entity =>
            {
                entity.Property(a => a.RelationshipType).IsRequired().HasMaxLength(100);
                entity.Property(a => a.SourceDetails).HasMaxLength(1000);
                entity.Property(a => a.Notes).HasMaxLength(2000);
                entity.Property(a => a.AllocationPercent).HasColumnType("decimal(5,2)");
                entity.Property(a => a.AssignedByName).HasMaxLength(200);
                entity.Property(a => a.RowVersion).IsRowVersion();
                entity.ToTable(t =>
                {
                    t.HasCheckConstraint("CK_ThirdPartyAttributions_ExactlyOneOwner",
                        "([LeadId] IS NOT NULL AND [CustomerId] IS NULL AND [BookingId] IS NULL) OR " +
                        "([LeadId] IS NULL AND [CustomerId] IS NOT NULL AND [BookingId] IS NULL) OR " +
                        "([LeadId] IS NULL AND [CustomerId] IS NULL AND [BookingId] IS NOT NULL)");
                    t.HasCheckConstraint("CK_ThirdPartyAttributions_Allocation", "[AllocationPercent] > 0 AND [AllocationPercent] <= 100");
                });
                entity.HasIndex(a => new { a.BookingId, a.PartnerId }).IsUnique().HasFilter("[BookingId] IS NOT NULL");
                entity.HasIndex(a => new { a.CustomerId, a.PartnerId }).IsUnique().HasFilter("[CustomerId] IS NOT NULL");
                entity.HasIndex(a => new { a.LeadId, a.PartnerId }).IsUnique().HasFilter("[LeadId] IS NOT NULL");
                entity.HasIndex(a => a.BookingId, "IX_ThirdPartyAttributions_Booking_Primary")
                    .IsUnique().HasFilter("[BookingId] IS NOT NULL AND [IsPrimary] = 1");
                entity.HasOne(a => a.Partner).WithMany(p => p.Attributions).HasForeignKey(a => a.PartnerId).OnDelete(DeleteBehavior.Restrict);
                entity.HasOne(a => a.Booking).WithMany(b => b.ThirdPartyAttributions).HasForeignKey(a => a.BookingId).OnDelete(DeleteBehavior.Restrict);
                entity.HasOne(a => a.Customer).WithMany().HasForeignKey(a => a.CustomerId).OnDelete(DeleteBehavior.Restrict);
                entity.HasOne(a => a.Lead).WithMany().HasForeignKey(a => a.LeadId).OnDelete(DeleteBehavior.Restrict);
            });

            modelBuilder.Entity<CommissionRule>(entity =>
            {
                entity.Property(r => r.Name).IsRequired().HasMaxLength(200);
                entity.Property(r => r.Description).HasMaxLength(1000);
                entity.Property(r => r.PartnerType).HasMaxLength(80);
                entity.Property(r => r.UnitCategory).HasMaxLength(100);
                entity.Property(r => r.BookingSource).HasConversion<int?>();
                entity.Property(r => r.CalculationType).HasConversion<int>();
                entity.Property(r => r.CalculationBasis).HasConversion<int>();
                entity.Property(r => r.EarningCondition).HasConversion<int>();
                entity.Property(r => r.PercentageRate).HasColumnType("decimal(9,6)");
                entity.Property(r => r.FixedAmount).HasColumnType("decimal(18,2)");
                entity.Property(r => r.MinimumCommission).HasColumnType("decimal(18,2)");
                entity.Property(r => r.MaximumCommission).HasColumnType("decimal(18,2)");
                entity.Property(r => r.MinimumCollectionPercent).HasColumnType("decimal(5,2)");
                entity.Property(r => r.EligibilityCondition).HasMaxLength(1000);
                entity.Property(r => r.Notes).HasMaxLength(2000);
                entity.Property(r => r.CreatedByName).HasMaxLength(200);
                entity.Property(r => r.RowVersion).IsRowVersion();
                entity.ToTable(t => t.HasCheckConstraint("CK_CommissionRules_Calculation",
                    "([CalculationType] = 0 AND [PercentageRate] IS NOT NULL AND [PercentageRate] > 0 AND [FixedAmount] IS NULL) OR " +
                    "([CalculationType] = 1 AND [FixedAmount] IS NOT NULL AND [FixedAmount] > 0 AND [PercentageRate] IS NULL)"));
                entity.HasIndex(r => new { r.IsActive, r.EffectiveFrom, r.EffectiveTo, r.Priority });
                entity.HasIndex(r => r.PartnerId);
                entity.HasIndex(r => r.ProjectId);
                entity.HasIndex(r => r.BookingId);
                entity.HasOne(r => r.Partner).WithMany(p => p.CommissionRules).HasForeignKey(r => r.PartnerId).OnDelete(DeleteBehavior.Restrict);
                entity.HasOne(r => r.Project).WithMany().HasForeignKey(r => r.ProjectId).OnDelete(DeleteBehavior.Restrict);
                entity.HasOne(r => r.Booking).WithMany(b => b.CommissionRules).HasForeignKey(r => r.BookingId).OnDelete(DeleteBehavior.Restrict);
            });

            modelBuilder.Entity<BookingCommission>(entity =>
            {
                entity.Property(c => c.PartnerNameSnapshot).IsRequired().HasMaxLength(200);
                entity.Property(c => c.PartnerTypeSnapshot).IsRequired().HasMaxLength(80);
                entity.Property(c => c.PartnerInternalCodeSnapshot).IsRequired().HasMaxLength(80);
                entity.Property(c => c.AllocationPercentSnapshot).HasColumnType("decimal(5,2)");
                entity.Property(c => c.RuleNameSnapshot).HasMaxLength(200);
                entity.Property(c => c.MinimumCommissionSnapshot).HasColumnType("decimal(18,2)");
                entity.Property(c => c.MaximumCommissionSnapshot).HasColumnType("decimal(18,2)");
                entity.Property(c => c.EligibilityConditionSnapshot).HasMaxLength(1000);
                entity.Property(c => c.ManualReason).HasMaxLength(2000);
                entity.Property(c => c.CalculationType).HasConversion<int>();
                entity.Property(c => c.CalculationBasis).HasConversion<int>();
                entity.Property(c => c.EarningCondition).HasConversion<int>();
                entity.Property(c => c.Status).HasConversion<int>();
                entity.Property(c => c.PercentageRate).HasColumnType("decimal(9,6)");
                entity.Property(c => c.FixedAmount).HasColumnType("decimal(18,2)");
                entity.Property(c => c.BasisAmount).HasColumnType("decimal(18,2)");
                entity.Property(c => c.CalculatedAmount).HasColumnType("decimal(18,2)");
                entity.Property(c => c.AdjustmentAmount).HasColumnType("decimal(18,2)");
                entity.Property(c => c.FinalAmount).HasColumnType("decimal(18,2)");
                entity.Property(c => c.ApprovedAmount).HasColumnType("decimal(18,2)");
                entity.Property(c => c.MinimumCollectionPercent).HasColumnType("decimal(5,2)");
                entity.Property(c => c.AdjustmentReason).HasMaxLength(2000);
                entity.Property(c => c.DecisionReason).HasMaxLength(2000);
                entity.Property(c => c.CancellationOrReversalReason).HasMaxLength(2000);
                entity.Property(c => c.CreatedByName).HasMaxLength(200);
                entity.Property(c => c.SubmittedByName).HasMaxLength(200);
                entity.Property(c => c.DecisionByName).HasMaxLength(200);
                entity.Property(c => c.RowVersion).IsRowVersion();
                entity.ToTable(t => t.HasCheckConstraint("CK_BookingCommissions_Amounts",
                    "[BasisAmount] > 0 AND [CalculatedAmount] >= 0 AND [FinalAmount] >= 0 AND ([ApprovedAmount] IS NULL OR [ApprovedAmount] >= 0) AND [AllocationPercentSnapshot] > 0 AND [AllocationPercentSnapshot] <= 100"));
                // Uniqueness is enforced only on the live commission for a (booking, partner):
                // a rejected, cancelled, or reversed row is closed history and must be allowed to
                // coexist with a corrected replacement. Without the filter, superseding a rejected
                // commission would violate the unique index at insert time.
                entity.HasIndex(c => new { c.BookingId, c.PartnerId })
                      .IsUnique()
                      .HasFilter($"[Status] <> {(int)BookingCommissionStatus.Rejected} " +
                                 $"AND [Status] <> {(int)BookingCommissionStatus.Cancelled} " +
                                 $"AND [Status] <> {(int)BookingCommissionStatus.Reversed}");
                entity.HasIndex(c => new { c.Status, c.CreatedAt });
                entity.HasIndex(c => c.RuleId);
                entity.HasIndex(c => c.RuleRevisionId);
                entity.HasOne(c => c.Booking).WithMany(b => b.Commissions).HasForeignKey(c => c.BookingId).OnDelete(DeleteBehavior.Restrict);
                entity.HasOne(c => c.Partner).WithMany(p => p.Commissions).HasForeignKey(c => c.PartnerId).OnDelete(DeleteBehavior.Restrict);
                entity.HasOne(c => c.Attribution).WithMany(a => a.Commissions).HasForeignKey(c => c.AttributionId).OnDelete(DeleteBehavior.Restrict);
                entity.HasOne(c => c.Rule).WithMany(r => r.Commissions).HasForeignKey(c => c.RuleId).OnDelete(DeleteBehavior.Restrict);
                entity.HasOne(c => c.RuleRevision).WithMany(r => r.Commissions).HasForeignKey(c => c.RuleRevisionId).OnDelete(DeleteBehavior.Restrict);
            });

            modelBuilder.Entity<CommissionRuleRevision>(entity =>
            {
                entity.Property(r => r.SnapshotJson).IsRequired();
                entity.Property(r => r.PreviousSnapshotJson);
                entity.Property(r => r.SnapshotHash).IsRequired().HasMaxLength(64).IsUnicode(false);
                entity.Property(r => r.ChangeReason).IsRequired().HasMaxLength(2000);
                entity.Property(r => r.ChangedByName).HasMaxLength(200);
                entity.HasIndex(r => new { r.RuleId, r.RevisionNumber }).IsUnique();
                entity.HasIndex(r => r.SnapshotHash);
                entity.HasOne(r => r.Rule).WithMany(r => r.Revisions).HasForeignKey(r => r.RuleId).OnDelete(DeleteBehavior.Restrict);
            });

            modelBuilder.Entity<CommissionPayout>(entity =>
            {
                entity.Property(p => p.Amount).HasColumnType("decimal(18,2)");
                entity.Property(p => p.PaymentMethod).HasConversion<int>();
                entity.Property(p => p.PaymentReference).HasMaxLength(200);
                entity.Property(p => p.DestinationBankNameSnapshot).HasMaxLength(150);
                entity.Property(p => p.DestinationAccountTitleSnapshot).HasMaxLength(150);
                entity.Property(p => p.DestinationAccountNumberSnapshot).HasMaxLength(100);
                entity.Property(p => p.DestinationIbanSnapshot).HasMaxLength(100);
                entity.Property(p => p.IdempotencyKey).IsRequired().HasMaxLength(80);
                entity.Property(p => p.Notes).HasMaxLength(2000);
                entity.Property(p => p.RecordedByName).HasMaxLength(200);
                entity.Property(p => p.RowVersion).IsRowVersion();
                entity.ToTable(t => t.HasCheckConstraint("CK_CommissionPayouts_Positive", "[Amount] > 0"));
                entity.HasIndex(p => p.IdempotencyKey).IsUnique();
                entity.HasIndex(p => new { p.CommissionId, p.PaymentDate });
                entity.HasIndex(p => p.FinanceAccountId);
                entity.HasOne(p => p.Commission).WithMany(c => c.Payouts).HasForeignKey(p => p.CommissionId).OnDelete(DeleteBehavior.Restrict);
                entity.HasOne(p => p.FinanceAccount).WithMany(a => a.CommissionPayouts).HasForeignKey(p => p.FinanceAccountId).OnDelete(DeleteBehavior.Restrict);
            });

            modelBuilder.Entity<CommissionPayoutReversal>(entity =>
            {
                entity.Property(r => r.Amount).HasColumnType("decimal(18,2)");
                entity.Property(r => r.Reason).IsRequired().HasMaxLength(2000);
                entity.Property(r => r.IdempotencyKey).IsRequired().HasMaxLength(80);
                entity.Property(r => r.ReversedByName).HasMaxLength(200);
                entity.ToTable(t => t.HasCheckConstraint("CK_CommissionPayoutReversals_Positive", "[Amount] > 0"));
                entity.HasIndex(r => r.IdempotencyKey).IsUnique();
                entity.HasIndex(r => new { r.PayoutId, r.ReversedAt });
                entity.HasOne(r => r.Payout).WithMany(p => p.Reversals).HasForeignKey(r => r.PayoutId).OnDelete(DeleteBehavior.Restrict);
            });

            modelBuilder.Entity<CustomerRebate>(entity =>
            {
                entity.Property(r => r.CalculationType).HasConversion<int>();
                entity.Property(r => r.CalculationBasis).HasConversion<int>();
                entity.Property(r => r.Method).HasConversion<int>();
                entity.Property(r => r.Status).HasConversion<int>();
                entity.Property(r => r.PercentageRate).HasColumnType("decimal(9,6)");
                entity.Property(r => r.FixedAmount).HasColumnType("decimal(18,2)");
                entity.Property(r => r.BasisAmount).HasColumnType("decimal(18,2)");
                entity.Property(r => r.CalculatedAmount).HasColumnType("decimal(18,2)");
                entity.Property(r => r.AdjustmentAmount).HasColumnType("decimal(18,2)");
                entity.Property(r => r.FinalAmount).HasColumnType("decimal(18,2)");
                entity.Property(r => r.ApprovedAmount).HasColumnType("decimal(18,2)");
                entity.Property(r => r.Reason).IsRequired().HasMaxLength(2000);
                entity.Property(r => r.AdjustmentReason).HasMaxLength(2000);
                entity.Property(r => r.Notes).HasMaxLength(2000);
                entity.Property(r => r.DecisionReason).HasMaxLength(2000);
                entity.Property(r => r.CancellationOrReversalReason).HasMaxLength(2000);
                entity.Property(r => r.CreatedByName).HasMaxLength(200);
                entity.Property(r => r.SubmittedByName).HasMaxLength(200);
                entity.Property(r => r.DecisionByName).HasMaxLength(200);
                entity.Property(r => r.RowVersion).IsRowVersion();
                entity.ToTable(t => t.HasCheckConstraint("CK_CustomerRebates_Amounts",
                    "[BasisAmount] > 0 AND [CalculatedAmount] >= 0 AND [FinalAmount] >= 0 AND ([ApprovedAmount] IS NULL OR [ApprovedAmount] >= 0)"));
                // Uniqueness is enforced only on the live rebate for a booking: a rejected,
                // cancelled, or reversed row is closed history and must be allowed to coexist with a
                // corrected replacement. Without the filter, superseding a returned/rejected rebate
                // would violate the unique index at insert time.
                entity.HasIndex(r => r.BookingId)
                      .IsUnique()
                      .HasFilter($"[Status] <> {(int)CustomerRebateStatus.Rejected} " +
                                 $"AND [Status] <> {(int)CustomerRebateStatus.Cancelled} " +
                                 $"AND [Status] <> {(int)CustomerRebateStatus.Reversed}");
                entity.HasIndex(r => new { r.Status, r.CreatedAt });
                entity.HasIndex(r => r.CustomerId);
                entity.HasOne(r => r.Booking).WithMany(b => b.Rebates).HasForeignKey(r => r.BookingId).OnDelete(DeleteBehavior.Restrict);
                entity.HasOne(r => r.Customer).WithMany().HasForeignKey(r => r.CustomerId).OnDelete(DeleteBehavior.Restrict);
            });

            modelBuilder.Entity<RebateDisbursement>(entity =>
            {
                entity.Property(d => d.Method).HasConversion<int>();
                entity.Property(d => d.PaymentMethod).HasConversion<int?>();
                entity.Property(d => d.Amount).HasColumnType("decimal(18,2)");
                entity.Property(d => d.Reference).HasMaxLength(200);
                entity.Property(d => d.IdempotencyKey).IsRequired().HasMaxLength(80);
                entity.Property(d => d.Notes).HasMaxLength(2000);
                entity.Property(d => d.RecordedByName).HasMaxLength(200);
                entity.Property(d => d.RowVersion).IsRowVersion();
                entity.ToTable(t => t.HasCheckConstraint("CK_RebateDisbursements_Positive", "[Amount] > 0"));
                entity.HasIndex(d => d.IdempotencyKey).IsUnique();
                entity.HasIndex(d => new { d.RebateId, d.AppliedAt });
                entity.HasIndex(d => d.FinanceAccountId);
                entity.HasIndex(d => d.InstallmentId);
                entity.HasOne(d => d.Rebate).WithMany(r => r.Disbursements).HasForeignKey(d => d.RebateId).OnDelete(DeleteBehavior.Restrict);
                entity.HasOne(d => d.FinanceAccount).WithMany(a => a.RebateDisbursements).HasForeignKey(d => d.FinanceAccountId).OnDelete(DeleteBehavior.Restrict);
                entity.HasOne(d => d.Installment).WithMany().HasForeignKey(d => d.InstallmentId).OnDelete(DeleteBehavior.Restrict);
            });

            modelBuilder.Entity<RebateDisbursementReversal>(entity =>
            {
                entity.Property(r => r.Amount).HasColumnType("decimal(18,2)");
                entity.Property(r => r.Reason).IsRequired().HasMaxLength(2000);
                entity.Property(r => r.IdempotencyKey).IsRequired().HasMaxLength(80);
                entity.Property(r => r.ReversedByName).HasMaxLength(200);
                entity.ToTable(t => t.HasCheckConstraint("CK_RebateDisbursementReversals_Positive", "[Amount] > 0"));
                entity.HasIndex(r => r.IdempotencyKey).IsUnique();
                entity.HasIndex(r => new { r.DisbursementId, r.ReversedAt });
                entity.HasOne(r => r.Disbursement).WithMany(d => d.Reversals).HasForeignKey(r => r.DisbursementId).OnDelete(DeleteBehavior.Restrict);
            });

            modelBuilder.Entity<FinancialEvidence>(entity =>
            {
                entity.Property(e => e.StoredFileName).IsRequired().HasMaxLength(100);
                entity.Property(e => e.OriginalFileName).IsRequired().HasMaxLength(180);
                entity.Property(e => e.ContentType).IsRequired().HasMaxLength(150);
                entity.Property(e => e.UploadedByName).HasMaxLength(200);
                entity.ToTable(t => t.HasCheckConstraint("CK_FinancialEvidence_ExactlyOneOwner",
                    "(CASE WHEN [CommissionId] IS NULL THEN 0 ELSE 1 END + " +
                    "CASE WHEN [PayoutId] IS NULL THEN 0 ELSE 1 END + " +
                    "CASE WHEN [RebateId] IS NULL THEN 0 ELSE 1 END + " +
                    "CASE WHEN [RebateDisbursementId] IS NULL THEN 0 ELSE 1 END) = 1"));
                entity.HasIndex(e => e.StoredFileName).IsUnique();
                entity.HasIndex(e => e.CommissionId);
                entity.HasIndex(e => e.PayoutId);
                entity.HasIndex(e => e.RebateId);
                entity.HasIndex(e => e.RebateDisbursementId);
                entity.HasOne(e => e.Commission).WithMany(c => c.Evidence).HasForeignKey(e => e.CommissionId).OnDelete(DeleteBehavior.Restrict);
                entity.HasOne(e => e.Payout).WithMany(p => p.Evidence).HasForeignKey(e => e.PayoutId).OnDelete(DeleteBehavior.Restrict);
                entity.HasOne(e => e.Rebate).WithMany(r => r.Evidence).HasForeignKey(e => e.RebateId).OnDelete(DeleteBehavior.Restrict);
                entity.HasOne(e => e.RebateDisbursement).WithMany(d => d.Evidence).HasForeignKey(e => e.RebateDisbursementId).OnDelete(DeleteBehavior.Restrict);
            });

            modelBuilder.Entity<FinancialWorkflowAuditEntry>(entity =>
            {
                entity.Property(a => a.Action).HasConversion<int>();
                entity.Property(a => a.PreviousCommissionStatus).HasConversion<int?>();
                entity.Property(a => a.NewCommissionStatus).HasConversion<int?>();
                entity.Property(a => a.PreviousRebateStatus).HasConversion<int?>();
                entity.Property(a => a.NewRebateStatus).HasConversion<int?>();
                entity.Property(a => a.PreviousAmount).HasColumnType("decimal(18,2)");
                entity.Property(a => a.NewAmount).HasColumnType("decimal(18,2)");
                // Unbounded: machine-generated rule change summaries record exact before/after values
                // for every field and can legitimately exceed a fixed cap; a length limit here would
                // make an otherwise-valid rule edit fail on save. Immutable append-only rows.
                entity.Property(a => a.Reason);
                entity.Property(a => a.PerformedByName).HasMaxLength(200);
                entity.HasIndex(a => new { a.BookingId, a.OccurredAt });
                entity.HasIndex(a => new { a.CommissionRuleId, a.OccurredAt });
                entity.HasIndex(a => new { a.CommissionRuleRevisionId, a.OccurredAt });
                entity.HasIndex(a => new { a.PartnerId, a.OccurredAt });
                entity.HasIndex(a => new { a.CommissionId, a.OccurredAt });
                entity.HasIndex(a => new { a.RebateId, a.OccurredAt });
                entity.HasOne(a => a.Partner).WithMany().HasForeignKey(a => a.PartnerId).OnDelete(DeleteBehavior.Restrict);
                entity.HasOne(a => a.Customer).WithMany().HasForeignKey(a => a.CustomerId).OnDelete(DeleteBehavior.Restrict);
                entity.HasOne(a => a.Booking).WithMany().HasForeignKey(a => a.BookingId).OnDelete(DeleteBehavior.Restrict);
                entity.HasOne(a => a.CommissionRule).WithMany().HasForeignKey(a => a.CommissionRuleId).OnDelete(DeleteBehavior.Restrict);
                entity.HasOne(a => a.CommissionRuleRevision).WithMany(r => r.AuditEntries).HasForeignKey(a => a.CommissionRuleRevisionId).OnDelete(DeleteBehavior.Restrict);
                entity.HasOne(a => a.Commission).WithMany().HasForeignKey(a => a.CommissionId).OnDelete(DeleteBehavior.Restrict);
                entity.HasOne(a => a.Payout).WithMany().HasForeignKey(a => a.PayoutId).OnDelete(DeleteBehavior.Restrict);
                entity.HasOne(a => a.Rebate).WithMany().HasForeignKey(a => a.RebateId).OnDelete(DeleteBehavior.Restrict);
                entity.HasOne(a => a.RebateDisbursement).WithMany().HasForeignKey(a => a.RebateDisbursementId).OnDelete(DeleteBehavior.Restrict);
            });
        }

        private void EnforceImmutableHistory()
        {
            if (ChangeTracker.Entries<CommissionRuleRevision>()
                .Any(e => e.State is EntityState.Modified or EntityState.Deleted))
                throw new InvalidOperationException("Commission rule revisions are immutable.");
            if (ChangeTracker.Entries<FinancialWorkflowAuditEntry>()
                .Any(e => e.State is EntityState.Modified or EntityState.Deleted))
                throw new InvalidOperationException("Financial workflow audit entries are append-only.");
            if (ChangeTracker.Entries<CustomerDocumentAuditEntry>()
                .Any(e => e.State is EntityState.Modified or EntityState.Deleted))
                throw new InvalidOperationException("Customer document audit entries are append-only.");
            if (ChangeTracker.Entries<OpeningBalanceAuditEntry>()
                .Any(e => e.State is EntityState.Modified or EntityState.Deleted))
                throw new InvalidOperationException("Opening balance audit entries are append-only.");
            if (ChangeTracker.Entries<CapitalTransaction>()
                .Any(e => e.State is EntityState.Modified or EntityState.Deleted))
                throw new InvalidOperationException("Capital transactions are immutable.");
        }

        public override int SaveChanges(bool acceptAllChangesOnSuccess)
        {
            EnforceImmutableHistory();
            return base.SaveChanges(acceptAllChangesOnSuccess);
        }

        public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess,
            CancellationToken cancellationToken = default)
        {
            EnforceImmutableHistory();
            return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
        }

        private static CustomerDocumentCategory SeedDocumentCategory(
            int id, string name, string code, bool required, int order, DateTime createdAt) => new()
        {
            Id = id,
            Name = name,
            Code = code,
            IsRequiredByDefault = required,
            DisplayOrder = order,
            AllowedFileTypes = ".pdf,.jpg,.jpeg,.png",
            MaxFileSizeBytes = 10 * 1024 * 1024,
            IsActive = true,
            AssignToNewCustomers = true,
            CreatedAt = createdAt
        };

        // Fixed timestamp: HasData must be deterministic or every `migrations add` produces
        // a spurious "changed" row for every seeded category.
        private static readonly DateTime WhtSeededAt = new(2026, 8, 11, 0, 0, 0, DateTimeKind.Utc);

        private static void ConfigureWithholdingTax(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<ExpenseCategory>(entity =>
            {
                entity.Property(c => c.Name).IsRequired().HasMaxLength(150);
                entity.Property(c => c.Code).IsRequired().HasMaxLength(80);
                entity.Property(c => c.Description).HasMaxLength(1000);
                entity.Property(c => c.TaxSection).HasMaxLength(30);
                entity.Property(c => c.FilerRate).HasColumnType("decimal(9,4)");
                entity.Property(c => c.NonFilerRate).HasColumnType("decimal(9,4)");
                entity.Property(c => c.AnnualThreshold).HasColumnType("decimal(18,2)");
                entity.Property(c => c.RowVersion).IsRowVersion();

                entity.HasIndex(c => c.Code).IsUnique();
                entity.HasIndex(c => new { c.IsActive, c.DisplayOrder });
                // The threshold is a per-vendor annual aggregate per tax section, so reporting and
                // the threshold check both group categories by section.
                entity.HasIndex(c => c.TaxSection);

                entity.HasData(SeedExpenseCategories());
            });

            modelBuilder.Entity<Vendor>(entity =>
            {
                entity.Property(v => v.Name).IsRequired().HasMaxLength(200);
                entity.Property(v => v.Ntn).HasMaxLength(30);
                entity.Property(v => v.Cnic).HasMaxLength(20);
                entity.Property(v => v.Phone).HasMaxLength(50);
                entity.Property(v => v.Address).HasMaxLength(500);
                entity.Property(v => v.Notes).HasMaxLength(1000);
                entity.Property(v => v.FilerStatus).HasConversion<int>();
                entity.Property(v => v.RowVersion).IsRowVersion();

                entity.HasIndex(v => v.Name).IsUnique();
                entity.HasIndex(v => new { v.IsActive, v.Name });
                // Filtered, so the many vendors without an NTN do not collide on NULL.
                entity.HasIndex(v => v.Ntn).IsUnique().HasFilter("[Ntn] IS NOT NULL");
            });

            modelBuilder.Entity<WhtDeposit>(entity =>
            {
                entity.Property(d => d.Amount).HasColumnType("decimal(18,2)");
                entity.Property(d => d.ChallanNumber).HasMaxLength(100);
                entity.Property(d => d.Notes).HasMaxLength(1000);
                entity.Property(d => d.RowVersion).IsRowVersion();

                entity.HasIndex(d => d.DepositDate);
                entity.HasIndex(d => d.FinanceAccountId);
                entity.HasIndex(d => d.ChallanNumber).IsUnique().HasFilter("[ChallanNumber] IS NOT NULL");

                entity.HasOne(d => d.FinanceAccount)
                      .WithMany(a => a.WhtDeposits)
                      .HasForeignKey(d => d.FinanceAccountId)
                      .OnDelete(DeleteBehavior.Restrict);
            });

            modelBuilder.Entity<FinanceSetting>(entity =>
            {
                entity.Property(s => s.WhtRatesConfirmedByName).HasMaxLength(200);
                entity.Property(s => s.RowVersion).IsRowVersion();
                // One row, always. The check constraint is what makes "singleton" true in the
                // database rather than only in the service that reads it.
                entity.ToTable(t => t.HasCheckConstraint("CK_FinanceSettings_Singleton", "[Id] = 1"));
                entity.HasData(new FinanceSetting
                {
                    Id = FinanceSetting.SingletonId,
                    FinancialYearStartMonth = 7
                });
            });
        }

        /// <summary>
        /// Starting withholding rates for the expense heads a property developer actually uses.
        /// <para>
        /// These are STARTING VALUES, not settled law. Published rates for the same head differ
        /// between sources and moved again under the last two Finance Acts, which is exactly why
        /// every rate here is editable in Finance ▸ Settings. The rate table shows an unverified
        /// banner until an accountant confirms them (<c>FinanceSetting.WhtRatesConfirmedAt</c>).
        /// </para>
        /// <para>
        /// Cement and steel sit in their own FBR band alongside FMCG, fertiliser, sugar and edible
        /// oil, at a materially lower rate than general goods — which is why "materials" cannot be
        /// one category.
        /// </para>
        /// </summary>
        private static ExpenseCategory[] SeedExpenseCategories() =>
        [
            // ── Land & statutory ──
            // These three carry the names the expense form offered before categories existed, so
            // the migration's name match links historic rows to them. None is withheld at the
            // point of payment, which is why each is flagged not-applicable rather than 0%.
            Head(60, "Land Acquisition", "land_acquisition", null, 0m, 0m, 0m, 5,
                isWhtApplicable: false,
                description: "Purchase of land or immovable property. Advance tax under s.236K is collected by the registering authority at transfer — the buyer withholds nothing from the seller here."),
            Head(61, "Permits & Approvals", "permits_approvals", null, 0m, 0m, 0m, 590,
                isWhtApplicable: false,
                description: "Fees paid to government and regulatory bodies. Payments to government are outside the s.153 withholding regime."),
            Head(62, "Taxes & Fees", "taxes_fees", null, 0m, 0m, 0m, 595,
                isWhtApplicable: false,
                description: "Statutory levies and government charges. Not withheld at the point of payment."),

            // ── Construction materials — s.153(1)(a), goods ──
            Head(1, "Cement", "cement", "153(1)(a)", 1.00m, 2.00m, 75_000m, 10),
            Head(2, "Steel / Iron / Rebar", "steel_rebar", "153(1)(a)", 1.00m, 2.00m, 75_000m, 20),
            Head(3, "Sand, Gravel & Aggregate", "sand_aggregate", "153(1)(a)", 5.00m, 10.00m, 75_000m, 30),
            Head(4, "Bricks & Blocks", "bricks_blocks", "153(1)(a)", 5.00m, 10.00m, 75_000m, 40),
            Head(5, "Tiles, Marble & Flooring", "tiles_marble", "153(1)(a)", 5.00m, 10.00m, 75_000m, 50),
            Head(6, "Wood & Timber", "wood_timber", "153(1)(a)", 5.00m, 10.00m, 75_000m, 60),
            Head(7, "Paint & Finishing Materials", "paint_finishing", "153(1)(a)", 5.00m, 10.00m, 75_000m, 70),
            Head(8, "Electrical Materials", "electrical_materials", "153(1)(a)", 5.00m, 10.00m, 75_000m, 80),
            Head(9, "Plumbing & Sanitary Materials", "plumbing_sanitary", "153(1)(a)", 5.00m, 10.00m, 75_000m, 90),
            Head(10, "Glass & Aluminium", "glass_aluminium", "153(1)(a)", 5.00m, 10.00m, 75_000m, 100),
            Head(11, "Hardware & Tools", "hardware_tools", "153(1)(a)", 5.00m, 10.00m, 75_000m, 110),
            Head(12, "Other Construction Materials", "other_materials", "153(1)(a)", 5.00m, 10.00m, 75_000m, 120),

            // ── Construction services & contracts ──
            Head(20, "Construction Contract (Company)", "contract_company", "153(1)(c)", 7.50m, 15.00m, 30_000m, 200),
            Head(21, "Construction Contract (Individual/AOP)", "contract_individual", "153(1)(c)", 8.00m, 16.00m, 30_000m, 210),
            Head(22, "Labour & Manpower", "labour_manpower", "153(1)(b)", 15.00m, 30.00m, 30_000m, 220),
            Head(23, "Architecture & Design Fee", "architecture_design", "153(1)(b)", 15.00m, 30.00m, 30_000m, 230),
            Head(24, "Engineering & Consultancy", "engineering_consultancy", "153(1)(b)", 15.00m, 30.00m, 30_000m, 240),
            Head(25, "Surveying & Soil Testing", "surveying_soil_testing", "153(1)(b)", 15.00m, 30.00m, 30_000m, 250),
            Head(26, "Machinery & Equipment Rent", "machinery_rent", "153(1)(b)", 15.00m, 30.00m, 30_000m, 260),
            Head(27, "Transport & Freight", "transport_freight", "153(1)(b)", 6.00m, 12.00m, 30_000m, 270),
            Head(28, "Security Services", "security_services", "153(1)(b)", 6.00m, 12.00m, 30_000m, 280),

            // ── Office & administration ──
            Head(40, "Office Rent", "office_rent", "155", 5.00m, 10.00m, 0m, 400),
            // s.149 is slab-based on each employee, not a flat supplier rate — payroll's job, not this form's.
            Head(41, "Salaries", "salaries", "149", 0m, 0m, 0m, 410, isWhtApplicable: false),
            // s.235 is collected at source by the utility company; the developer withholds nothing.
            Head(42, "Utilities", "utilities", "235", 0m, 0m, 0m, 420, isWhtApplicable: false),
            Head(43, "Printing & Stationery", "printing_stationery", "153(1)(a)", 5.00m, 10.00m, 75_000m, 430),
            Head(44, "Office Supplies", "office_supplies", "153(1)(a)", 5.00m, 10.00m, 75_000m, 440),
            Head(45, "Office Equipment", "office_equipment", "153(1)(a)", 5.00m, 10.00m, 75_000m, 450),
            Head(46, "Office Renovation", "office_renovation", "153(1)(c)", 7.50m, 15.00m, 30_000m, 460),
            Head(47, "Software & Web", "software_web", "153(1)(b)", 4.00m, 8.00m, 30_000m, 470),
            Head(48, "Marketing & Advertisement", "marketing_advertisement", "153(1)(b)", 15.00m, 30.00m, 30_000m, 480),
            Head(49, "Business Promotion", "business_promotion", "153(1)(b)", 15.00m, 30.00m, 30_000m, 490),
            Head(50, "Legal & Professional", "legal_professional", "153(1)(b)", 15.00m, 30.00m, 30_000m, 500),
            Head(51, "Camera Equipment Rent", "camera_rent", "153(1)(b)", 15.00m, 30.00m, 30_000m, 510),
            Head(52, "Entertainment & Food", "entertainment_food", "153(1)(b)", 6.00m, 12.00m, 30_000m, 520),
            Head(53, "Repairs & Maintenance", "repairs_maintenance", "153(1)(b)", 15.00m, 30.00m, 30_000m, 530),
            Head(54, "Rebate / Commission", "rebate_commission", "233", 12.00m, 24.00m, 0m, 540),

            // ── Heads with no withholding section. Flagged not-applicable rather than left at 0%
            //    so the form says "no tax withheld" instead of showing an empty tax block.
            Head(55, "Site Expenses", "site_expenses", null, 0m, 0m, 0m, 550, isWhtApplicable: false),
            Head(56, "Preliminary", "preliminary", null, 0m, 0m, 0m, 560, isWhtApplicable: false),
            Head(57, "Donation / Charity", "donation_charity", null, 0m, 0m, 0m, 570, isWhtApplicable: false),
            Head(58, "Miscellaneous", "miscellaneous", null, 0m, 0m, 0m, 580, isWhtApplicable: false),
        ];

        private static ExpenseCategory Head(
            int id, string name, string code, string? section,
            decimal filerRate, decimal nonFilerRate, decimal threshold, int order,
            bool isWhtApplicable = true, string? description = null) => new()
        {
            Id = id,
            Name = name,
            Code = code,
            Description = description,
            TaxSection = section,
            IsWhtApplicable = isWhtApplicable,
            FilerRate = filerRate,
            NonFilerRate = nonFilerRate,
            AnnualThreshold = threshold,
            DisplayOrder = order,
            IsActive = true,
            CreatedAt = WhtSeededAt
        };

        private static void ConfigureLeadManagement(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<LeadSource>(entity =>
            {
                entity.Property(s => s.Code).IsRequired().HasMaxLength(50);
                entity.Property(s => s.Name).IsRequired().HasMaxLength(100);
                entity.Property(s => s.CustomerSource).HasConversion<int>();
                entity.HasIndex(s => s.Code).IsUnique();
            });

            modelBuilder.Entity<LeadClosureReason>(entity =>
            {
                entity.Property(r => r.Code).IsRequired().HasMaxLength(50);
                entity.Property(r => r.Name).IsRequired().HasMaxLength(150);
                entity.Property(r => r.Kind).HasConversion<int>();
                entity.HasIndex(r => r.Code).IsUnique();
            });

            modelBuilder.Entity<Lead>(entity =>
            {
                entity.Property(l => l.LeadReference).IsRequired().HasMaxLength(50);
                entity.Property(l => l.FirstName).IsRequired().HasMaxLength(100);
                entity.Property(l => l.LastName).HasMaxLength(100);
                entity.Property(l => l.Phone).IsRequired().HasMaxLength(50);
                entity.Property(l => l.NormalizedPhone).IsRequired().HasMaxLength(50);
                entity.Property(l => l.WhatsappNumber).HasMaxLength(50);
                entity.Property(l => l.NormalizedWhatsapp).HasMaxLength(50);
                entity.Property(l => l.Email).HasMaxLength(200);
                entity.Property(l => l.NormalizedEmail).HasMaxLength(200);
                entity.Property(l => l.Address).HasMaxLength(500);
                entity.Property(l => l.City).HasMaxLength(100);
                entity.Property(l => l.PreferredContactTime).HasMaxLength(100);
                entity.Property(l => l.SourceDetails).HasMaxLength(500);
                entity.Property(l => l.CampaignName).HasMaxLength(200);
                entity.Property(l => l.CampaignReference).HasMaxLength(200);
                entity.Property(l => l.AdReference).HasMaxLength(200);
                entity.Property(l => l.ExternalProvider).HasMaxLength(50);
                entity.Property(l => l.ExternalLeadId).HasMaxLength(200);
                entity.Property(l => l.ExternalFormReference).HasMaxLength(200);
                entity.Property(l => l.IntegrationPayload).HasMaxLength(4000);
                entity.Property(l => l.IntegrationError).HasMaxLength(1000);
                entity.Property(l => l.PropertyType).HasMaxLength(100);
                entity.Property(l => l.PreferredLocation).HasMaxLength(200);
                entity.Property(l => l.Notes).HasMaxLength(2000);
                entity.Property(l => l.LastActivitySummary).HasMaxLength(300);
                entity.Property(l => l.NextActionSummary).HasMaxLength(300);
                entity.Property(l => l.ClosureNotes).HasMaxLength(1000);
                entity.Property(l => l.BudgetMin).HasColumnType("decimal(18,2)");
                entity.Property(l => l.BudgetMax).HasColumnType("decimal(18,2)");
                entity.Property(l => l.PreferredContactMethod).HasConversion<int>();
                entity.Property(l => l.PurchaseIntent).HasConversion<int>();
                entity.Property(l => l.AssignmentState).HasConversion<int>();
                entity.Property(l => l.Stage).HasConversion<int>();
                entity.Property(l => l.Qualification).HasConversion<int>();
                entity.Property(l => l.IntegrationStatus).HasConversion<int>();
                entity.Property(l => l.RowVersion).IsRowVersion();

                entity.HasIndex(l => l.LeadReference).IsUnique();
                // Duplicate detection and the "find my lead" lookups run on the normalised
                // identifiers, never on the display values.
                entity.HasIndex(l => l.NormalizedPhone);
                entity.HasIndex(l => l.NormalizedWhatsapp);
                entity.HasIndex(l => l.NormalizedEmail);
                entity.HasIndex(l => l.Stage);
                entity.HasIndex(l => l.CreatedAt);
                entity.HasIndex(l => new { l.AssignedEmployeeId, l.Stage });
                entity.HasIndex(l => new { l.AssignedTeamId, l.Stage });
                entity.HasIndex(l => new { l.Stage, l.CreatedAt });
                entity.HasIndex(l => l.NextActionAt);
                entity.HasIndex(l => l.LastActivityAt);
                entity.HasIndex(l => l.LeadSourceId);
                // The same external submission must never produce two leads, whatever the
                // provider does with retries.
                entity.HasIndex(l => new { l.ExternalProvider, l.ExternalLeadId })
                      .IsUnique()
                      .HasFilter("[ExternalProvider] IS NOT NULL AND [ExternalLeadId] IS NOT NULL");
                // One booking can only ever be the conversion target of one lead.
                entity.HasIndex(l => l.ConvertedBookingId)
                      .IsUnique()
                      .HasFilter("[ConvertedBookingId] IS NOT NULL");

                entity.HasOne(l => l.Source)
                      .WithMany()
                      .HasForeignKey(l => l.LeadSourceId)
                      .OnDelete(DeleteBehavior.Restrict);
                entity.HasOne(l => l.ClosureReason)
                      .WithMany()
                      .HasForeignKey(l => l.ClosureReasonId)
                      .OnDelete(DeleteBehavior.Restrict);
                entity.HasOne(l => l.AssignedEmployee)
                      .WithMany()
                      .HasForeignKey(l => l.AssignedEmployeeId)
                      .OnDelete(DeleteBehavior.Restrict);
                entity.HasOne(l => l.AssignedTeam)
                      .WithMany()
                      .HasForeignKey(l => l.AssignedTeamId)
                      .OnDelete(DeleteBehavior.SetNull);
                entity.HasOne(l => l.InterestedProject)
                      .WithMany()
                      .HasForeignKey(l => l.InterestedProjectId)
                      .OnDelete(DeleteBehavior.SetNull);
                entity.HasOne(l => l.InterestedUnit)
                      .WithMany()
                      .HasForeignKey(l => l.InterestedUnitId)
                      .OnDelete(DeleteBehavior.SetNull);
                entity.HasOne(l => l.ConvertedCustomer)
                      .WithMany()
                      .HasForeignKey(l => l.ConvertedCustomerId)
                      .OnDelete(DeleteBehavior.NoAction);
                entity.HasOne(l => l.ConvertedBooking)
                      .WithMany()
                      .HasForeignKey(l => l.ConvertedBookingId)
                      .OnDelete(DeleteBehavior.NoAction);
            });

            modelBuilder.Entity<LeadExternalSubmission>(entity =>
            {
                entity.Property(s => s.Provider).IsRequired().HasMaxLength(50);
                entity.Property(s => s.ExternalLeadId).IsRequired().HasMaxLength(200);
                entity.Property(s => s.ExternalFormReference).HasMaxLength(200);

                entity.HasIndex(s => new { s.Provider, s.ExternalLeadId }).IsUnique();
                entity.HasIndex(s => new { s.LeadId, s.ReceivedAt });

                entity.HasOne(s => s.Lead)
                      .WithMany(l => l.ExternalSubmissions)
                      .HasForeignKey(s => s.LeadId)
                      .OnDelete(DeleteBehavior.Cascade);
            });

            modelBuilder.Entity<LeadActivity>(entity =>
            {
                entity.Property(a => a.Summary).IsRequired().HasMaxLength(300);
                entity.Property(a => a.Notes).HasMaxLength(2000);
                entity.Property(a => a.PreviousValue).HasMaxLength(300);
                entity.Property(a => a.NewValue).HasMaxLength(300);
                entity.Property(a => a.PerformedByName).HasMaxLength(200);
                entity.Property(a => a.Type).HasConversion<int>();
                entity.Property(a => a.Channel).HasConversion<int>();

                entity.HasIndex(a => new { a.LeadId, a.OccurredAt });
                entity.HasIndex(a => a.Type);

                entity.HasOne(a => a.Lead)
                      .WithMany(l => l.Activities)
                      .HasForeignKey(a => a.LeadId)
                      .OnDelete(DeleteBehavior.Cascade);
            });

            modelBuilder.Entity<LeadAssignmentHistory>(entity =>
            {
                entity.Property(h => h.Reason).HasMaxLength(500);
                entity.Property(h => h.AssignedByName).HasMaxLength(200);
                entity.HasIndex(h => new { h.LeadId, h.AssignedAt });

                entity.HasOne(h => h.Lead)
                      .WithMany(l => l.AssignmentHistory)
                      .HasForeignKey(h => h.LeadId)
                      .OnDelete(DeleteBehavior.Cascade);
            });

            modelBuilder.Entity<LeadCommunication>(entity =>
            {
                entity.Property(c => c.Summary).IsRequired().HasMaxLength(2000);
                entity.Property(c => c.CustomerResponse).HasMaxLength(2000);
                entity.Property(c => c.NextAction).HasMaxLength(500);
                entity.Property(c => c.ExternalProvider).HasMaxLength(50);
                entity.Property(c => c.ExternalMessageId).HasMaxLength(200);
                entity.Property(c => c.Channel).HasConversion<int>();
                entity.Property(c => c.Direction).HasConversion<int>();

                entity.HasIndex(c => new { c.LeadId, c.OccurredAt });
                // Replaying the same inbound provider message must not duplicate history.
                entity.HasIndex(c => new { c.ExternalProvider, c.ExternalMessageId })
                      .IsUnique()
                      .HasFilter("[ExternalProvider] IS NOT NULL AND [ExternalMessageId] IS NOT NULL");

                entity.HasOne(c => c.Lead)
                      .WithMany(l => l.Communications)
                      .HasForeignKey(c => c.LeadId)
                      .OnDelete(DeleteBehavior.Cascade);
                entity.HasOne(c => c.Employee)
                      .WithMany()
                      .HasForeignKey(c => c.EmployeeId)
                      .OnDelete(DeleteBehavior.Restrict);
            });

            modelBuilder.Entity<LeadFollowUp>(entity =>
            {
                entity.Property(f => f.Title).IsRequired().HasMaxLength(200);
                entity.Property(f => f.Notes).HasMaxLength(1000);
                entity.Property(f => f.Outcome).HasMaxLength(1000);
                entity.Property(f => f.Type).HasConversion<int>();
                entity.Property(f => f.Status).HasConversion<int>();
                entity.Property(f => f.Priority).HasConversion<int>();

                entity.HasIndex(f => new { f.AssignedEmployeeId, f.Status, f.DueAt });
                entity.HasIndex(f => new { f.LeadId, f.Status });
                entity.HasIndex(f => new { f.Status, f.DueAt });

                entity.HasOne(f => f.Lead)
                      .WithMany(l => l.FollowUps)
                      .HasForeignKey(f => f.LeadId)
                      .OnDelete(DeleteBehavior.Cascade);
                entity.HasOne(f => f.AssignedEmployee)
                      .WithMany()
                      .HasForeignKey(f => f.AssignedEmployeeId)
                      .OnDelete(DeleteBehavior.Restrict);
            });

            modelBuilder.Entity<LeadSiteVisit>(entity =>
            {
                entity.Property(v => v.MeetingLocation).IsRequired().HasMaxLength(300);
                entity.Property(v => v.CustomerAttendees).HasMaxLength(500);
                entity.Property(v => v.InternalAttendees).HasMaxLength(500);
                entity.Property(v => v.Notes).HasMaxLength(1000);
                entity.Property(v => v.OutcomeNotes).HasMaxLength(1000);
                entity.Property(v => v.CustomerFeedback).HasMaxLength(1000);
                entity.Property(v => v.NextAction).HasMaxLength(500);
                entity.Property(v => v.CancellationReason).HasMaxLength(500);
                entity.Property(v => v.Status).HasConversion<int>();
                entity.Property(v => v.Outcome).HasConversion<int>();

                entity.HasIndex(v => new { v.LeadId, v.ScheduledAt });
                entity.HasIndex(v => new { v.AssignedEmployeeId, v.Status, v.ScheduledAt });
                entity.HasIndex(v => new { v.Status, v.ScheduledAt });

                entity.HasOne(v => v.Lead)
                      .WithMany(l => l.SiteVisits)
                      .HasForeignKey(v => v.LeadId)
                      .OnDelete(DeleteBehavior.Cascade);
                entity.HasOne(v => v.AssignedEmployee)
                      .WithMany()
                      .HasForeignKey(v => v.AssignedEmployeeId)
                      .OnDelete(DeleteBehavior.Restrict);
                entity.HasOne(v => v.Project)
                      .WithMany()
                      .HasForeignKey(v => v.ProjectId)
                      .OnDelete(DeleteBehavior.SetNull);
                entity.HasOne(v => v.Unit)
                      .WithMany()
                      .HasForeignKey(v => v.UnitId)
                      .OnDelete(DeleteBehavior.SetNull);
            });

            modelBuilder.Entity<LeadDocument>(entity =>
            {
                entity.Property(d => d.StoredFileName).IsRequired().HasMaxLength(100);
                entity.Property(d => d.OriginalFileName).IsRequired().HasMaxLength(180);
                entity.Property(d => d.ContentType).IsRequired().HasMaxLength(150);
                entity.Property(d => d.Description).HasMaxLength(500);
                entity.Property(d => d.UploadedByName).HasMaxLength(200);
                entity.Property(d => d.Category).HasConversion<int>();

                entity.HasIndex(d => new { d.LeadId, d.UploadedAt });
                entity.HasIndex(d => d.StoredFileName).IsUnique();

                entity.HasOne(d => d.Lead)
                      .WithMany(l => l.Documents)
                      .HasForeignKey(d => d.LeadId)
                      .OnDelete(DeleteBehavior.Cascade);
                entity.HasOne(d => d.Communication)
                      .WithMany(c => c.Attachments)
                      .HasForeignKey(d => d.CommunicationId)
                      .OnDelete(DeleteBehavior.NoAction);
            });

            modelBuilder.Entity<LeadComment>(entity =>
            {
                entity.Property(c => c.Body).IsRequired().HasMaxLength(4000);
                entity.Property(c => c.AuthorName).HasMaxLength(200);
                entity.HasIndex(c => new { c.LeadId, c.CreatedAt });

                entity.HasOne(c => c.Lead)
                      .WithMany(l => l.Comments)
                      .HasForeignKey(c => c.LeadId)
                      .OnDelete(DeleteBehavior.Cascade);
                entity.HasOne(c => c.ParentComment)
                      .WithMany()
                      .HasForeignKey(c => c.ParentCommentId)
                      .OnDelete(DeleteBehavior.NoAction);
            });

            modelBuilder.Entity<LeadCommentMention>(entity =>
            {
                entity.HasIndex(m => new { m.LeadCommentId, m.MentionedUserId }).IsUnique();
                entity.HasIndex(m => m.MentionedUserId);

                entity.HasOne(m => m.LeadComment)
                      .WithMany(c => c.Mentions)
                      .HasForeignKey(m => m.LeadCommentId)
                      .OnDelete(DeleteBehavior.Cascade);
                entity.HasOne(m => m.MentionedUser)
                      .WithMany()
                      .HasForeignKey(m => m.MentionedUserId)
                      .OnDelete(DeleteBehavior.NoAction);
            });

            SeedLeadConfiguration(modelBuilder);
        }

        /// <summary>
        /// The central notification platform. Two database-level guarantees carry most of the
        /// reliability story: <c>Notifications.DedupKey</c> is unique, so the same business
        /// event can never produce two notifications for one person; and
        /// <c>(NotificationId, Channel)</c> is unique, so retrying a failed email can never
        /// re-send a push that already worked.
        /// </summary>
        private static void ConfigureNotifications(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Notification>(entity =>
            {
                entity.Property(n => n.Title).IsRequired().HasMaxLength(200);
                entity.Property(n => n.Message).IsRequired().HasMaxLength(2000);
                entity.Property(n => n.DeepLink).HasMaxLength(500);
                entity.Property(n => n.DedupKey).IsRequired().HasMaxLength(200);
                entity.Property(n => n.DataJson).HasMaxLength(4000);
                entity.Property(n => n.RecipientEmail).HasMaxLength(200);
                entity.Property(n => n.RecipientName).HasMaxLength(200);
                entity.Property(n => n.Category).HasConversion<int>();
                entity.Property(n => n.Type).HasConversion<int>();
                entity.Property(n => n.Priority).HasConversion<int>();
                entity.Property(n => n.Module).HasConversion<int>();
                entity.Property(n => n.EntityType).HasConversion<int>();
                entity.Property(n => n.Channels).HasConversion<int>();

                entity.HasIndex(n => n.DedupKey).IsUnique();
                // The inbox always reads "mine, newest first", optionally unread or by category.
                entity.HasIndex(n => new { n.RecipientUserId, n.IsRead, n.CreatedAt });
                entity.HasIndex(n => new { n.RecipientUserId, n.Category, n.CreatedAt });
                entity.HasIndex(n => new { n.EntityType, n.EntityId });
                entity.HasIndex(n => n.NotificationJobId);
                entity.HasIndex(n => n.CreatedAt);

                entity.HasOne(n => n.Recipient)
                      .WithMany()
                      .HasForeignKey(n => n.RecipientUserId)
                      // Notification and delivery history is an audit record and must survive
                      // account deletion. The nullable recipient id is cleared instead.
                      .OnDelete(DeleteBehavior.SetNull);

                entity.HasOne(n => n.Job)
                      .WithMany()
                      .HasForeignKey(n => n.NotificationJobId)
                      .OnDelete(DeleteBehavior.SetNull);
            });

            modelBuilder.Entity<NotificationDelivery>(entity =>
            {
                entity.Property(d => d.Channel).HasConversion<int>();
                entity.Property(d => d.Status).HasConversion<int>();
                entity.Property(d => d.Target).HasMaxLength(300);
                entity.Property(d => d.ProviderReference).HasMaxLength(200);
                entity.Property(d => d.FailureReason).HasMaxLength(1000);
                entity.Property(d => d.LockedBy).HasMaxLength(100);
                entity.Property(d => d.RowVersion).IsRowVersion();

                entity.HasIndex(d => new { d.NotificationId, d.Channel }).IsUnique();
                // The worker's claim query: "anything due, not locked, in a runnable state".
                entity.HasIndex(d => new { d.Status, d.AvailableAt });
                entity.HasIndex(d => d.LockedUntil);

                entity.HasOne(d => d.Notification)
                      .WithMany(n => n.Deliveries)
                      .HasForeignKey(d => d.NotificationId)
                      .OnDelete(DeleteBehavior.Cascade);
            });

            modelBuilder.Entity<NotificationTemplate>(entity =>
            {
                entity.Property(t => t.Name).IsRequired().HasMaxLength(150);
                entity.Property(t => t.Subject).IsRequired().HasMaxLength(300);
                entity.Property(t => t.Heading).HasMaxLength(300);
                entity.Property(t => t.Body).IsRequired().HasMaxLength(8000);
                entity.Property(t => t.ActionText).HasMaxLength(80);
                entity.Property(t => t.ActionUrl).HasMaxLength(500);
                entity.Property(t => t.Footer).HasMaxLength(2000);
                entity.Property(t => t.IconUrl).HasMaxLength(500);
                entity.Property(t => t.BadgeUrl).HasMaxLength(500);
                entity.Property(t => t.Type).HasConversion<int>();
                entity.Property(t => t.Channel).HasConversion<int>();
                entity.Property(t => t.Category).HasConversion<int>();

                entity.HasIndex(t => new { t.Type, t.Channel }).IsUnique();
            });

            modelBuilder.Entity<NotificationRule>(entity =>
            {
                entity.Property(r => r.Type).HasConversion<int>();
                entity.Property(r => r.Priority).HasConversion<int>();
                entity.HasIndex(r => r.Type).IsUnique();
            });

            modelBuilder.Entity<NotificationPreference>(entity =>
            {
                entity.Property(p => p.Category).HasConversion<int>();
                entity.HasIndex(p => new { p.UserId, p.Category }).IsUnique();

                entity.HasOne(p => p.User)
                      .WithMany()
                      .HasForeignKey(p => p.UserId)
                      .OnDelete(DeleteBehavior.Cascade);
            });

            modelBuilder.Entity<PushSubscription>(entity =>
            {
                entity.Property(s => s.Endpoint).IsRequired().HasMaxLength(400);
                entity.Property(s => s.P256dh).IsRequired().HasMaxLength(200);
                entity.Property(s => s.Auth).IsRequired().HasMaxLength(100);
                entity.Property(s => s.DeviceLabel).HasMaxLength(150);
                entity.Property(s => s.DeactivationReason).HasMaxLength(300);

                // One endpoint belongs to exactly one login at a time: re-registering the same
                // browser after a different user signs in moves it, so a shared computer never
                // delivers the previous user's notifications.
                entity.HasIndex(s => s.Endpoint).IsUnique();
                entity.HasIndex(s => new { s.UserId, s.IsActive });

                entity.HasOne(s => s.User)
                      .WithMany()
                      .HasForeignKey(s => s.UserId)
                      .OnDelete(DeleteBehavior.Cascade);
            });

            modelBuilder.Entity<NotificationSetting>(entity =>
            {
                entity.Property(s => s.Key).IsRequired().HasMaxLength(100);
                entity.Property(s => s.Value).HasMaxLength(2000);
                entity.HasIndex(s => s.Key).IsUnique();
            });

            modelBuilder.Entity<NotificationJob>(entity =>
            {
                entity.Property(j => j.Title).IsRequired().HasMaxLength(200);
                entity.Property(j => j.Message).IsRequired().HasMaxLength(2000);
                entity.Property(j => j.ActionText).HasMaxLength(80);
                entity.Property(j => j.ActionUrl).HasMaxLength(500);
                entity.Property(j => j.AudienceJson).HasMaxLength(4000);
                entity.Property(j => j.FailureReason).HasMaxLength(1000);
                entity.Property(j => j.RequestKey).HasMaxLength(100);
                entity.Property(j => j.LockedBy).HasMaxLength(100);
                entity.Property(j => j.Status).HasConversion<int>();
                entity.Property(j => j.Type).HasConversion<int>();
                entity.Property(j => j.Category).HasConversion<int>();
                entity.Property(j => j.Priority).HasConversion<int>();
                entity.Property(j => j.Channels).HasConversion<int>();
                entity.Property(j => j.AudienceType).HasConversion<int>();
                entity.Property(j => j.RowVersion).IsRowVersion();

                entity.HasIndex(j => new { j.Status, j.ScheduledAt });
                // A double-clicked Send arrives twice with one token; the database keeps one.
                entity.HasIndex(j => j.RequestKey)
                      .IsUnique()
                      .HasFilter("[RequestKey] IS NOT NULL");
            });

            modelBuilder.Entity<EmailSuppression>(entity =>
            {
                entity.Property(s => s.Email).IsRequired().HasMaxLength(200);
                entity.Property(s => s.Reason).IsRequired().HasMaxLength(300);
                entity.HasIndex(s => s.Email).IsUnique();
            });

            modelBuilder.Entity<NotificationAuditEntry>(entity =>
            {
                entity.Property(a => a.Area).IsRequired().HasMaxLength(50);
                entity.Property(a => a.Action).IsRequired().HasMaxLength(100);
                entity.Property(a => a.Details).HasMaxLength(2000);
                entity.Property(a => a.PerformedByName).HasMaxLength(200);
                entity.HasIndex(a => a.OccurredAt);
                entity.HasIndex(a => new { a.Area, a.OccurredAt });
            });
        }

        // Fixed timestamp: HasData must be deterministic or every `migrations add` produces
        // a spurious update for these rows.
        private static readonly DateTime SeedDate = new(2026, 7, 26, 0, 0, 0, DateTimeKind.Utc);

        private static void SeedLeadConfiguration(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<LeadSource>().HasData(
                NewSource(1, "manual", "Manual Entry", 1, CustomerSource.Other),
                NewSource(2, "walk_in", "Office Walk-in", 2, CustomerSource.WalkIn),
                NewSource(3, "phone", "Phone Call", 3, CustomerSource.Phone),
                NewSource(4, "referral", "Referral", 4, CustomerSource.Referral),
                NewSource(5, "website", "Website Inquiry", 5, CustomerSource.Website),
                NewSource(6, "facebook", "Facebook", 6, CustomerSource.Other),
                NewSource(7, "instagram", "Instagram", 7, CustomerSource.Other),
                NewSource(8, "whatsapp", "WhatsApp", 8, CustomerSource.Other),
                NewSource(9, "property_portal", "Property Portal", 9, CustomerSource.Other),
                NewSource(10, "broker", "Broker / Agent", 10, CustomerSource.Referral),
                NewSource(11, "campaign", "Marketing Campaign", 11, CustomerSource.Other),
                NewSource(12, "exhibition", "Exhibition / Event", 12, CustomerSource.Other),
                NewSource(13, "other", "Other", 13, CustomerSource.Other));

            modelBuilder.Entity<LeadClosureReason>().HasData(
                NewReason(1, "budget_issue", "Budget issue", 1, LeadClosureReasonKind.Both),
                NewReason(2, "not_interested", "Not interested", 2, LeadClosureReasonKind.Lost),
                NewReason(3, "purchased_elsewhere", "Purchased elsewhere", 3, LeadClosureReasonKind.Lost),
                NewReason(4, "location_unsuitable", "Location unsuitable", 4, LeadClosureReasonKind.Lost),
                NewReason(5, "payment_plan_unsuitable", "Payment plan unsuitable", 5, LeadClosureReasonKind.Both),
                NewReason(6, "unable_to_contact", "Unable to contact", 6, LeadClosureReasonKind.Both),
                NewReason(7, "invalid_information", "Invalid information", 7, LeadClosureReasonKind.Lost),
                NewReason(8, "duplicate", "Duplicate", 8, LeadClosureReasonKind.Lost),
                NewReason(9, "delayed_decision", "Delayed decision", 9, LeadClosureReasonKind.Dormant),
                NewReason(10, "other", "Other", 10, LeadClosureReasonKind.Both));
        }

        private static LeadSource NewSource(int id, string code, string name, int order, CustomerSource customerSource) =>
            new()
            {
                Id = id,
                Code = code,
                Name = name,
                DisplayOrder = order,
                IsActive = true,
                IsSystem = true,
                CustomerSource = customerSource,
                CreatedAt = SeedDate
            };

        private static LeadClosureReason NewReason(int id, string code, string name, int order, LeadClosureReasonKind kind) =>
            new()
            {
                Id = id,
                Code = code,
                Name = name,
                DisplayOrder = order,
                IsActive = true,
                IsSystem = true,
                Kind = kind,
                CreatedAt = SeedDate
            };
    }
}

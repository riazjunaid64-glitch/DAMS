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
        public DbSet<ManualRevenue> ManualRevenues { get; set; }
        public DbSet<FinanceAttachment> FinanceAttachments { get; set; }
        public DbSet<FinanceAccount> FinanceAccounts { get; set; }
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
                entity.HasOne(p => p.Booking)
                      .WithMany(b => b.Payments)
                      .HasForeignKey(p => p.BookingId)
                      .OnDelete(DeleteBehavior.Cascade);
                entity.HasOne(p => p.Installment)
                      .WithMany(i => i.Payments)
                      .HasForeignKey(p => p.InstallmentId)
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

                entity.HasIndex(e => e.ProjectId);
                entity.HasIndex(e => e.Date);
                entity.HasIndex(e => e.Category);
                entity.HasIndex(e => e.FinanceAccountId);

                entity.HasOne(e => e.Project)
                      .WithMany()
                      .HasForeignKey(e => e.ProjectId)
                      .OnDelete(DeleteBehavior.SetNull);

                entity.HasOne(e => e.FinanceAccount)
                      .WithMany(a => a.Expenses)
                      .HasForeignKey(e => e.FinanceAccountId)
                      .OnDelete(DeleteBehavior.Restrict);
            });

            modelBuilder.Entity<ManualRevenue>(entity =>
            {
                entity.Property(r => r.Amount).HasColumnType("decimal(18,2)");
                entity.Property(r => r.RevenueType).IsRequired().HasMaxLength(100);
                entity.Property(r => r.Description).HasMaxLength(1000);
                entity.Property(r => r.Reference).HasMaxLength(200);

                entity.HasIndex(r => r.ProjectId);
                entity.HasIndex(r => r.Date);
                entity.HasIndex(r => r.RevenueType);
                entity.HasIndex(r => r.FinanceAccountId);

                entity.HasOne(r => r.Project)
                      .WithMany()
                      .HasForeignKey(r => r.ProjectId)
                      .OnDelete(DeleteBehavior.SetNull);

                entity.HasOne(r => r.FinanceAccount)
                      .WithMany(a => a.ManualRevenues)
                      .HasForeignKey(r => r.FinanceAccountId)
                      .OnDelete(DeleteBehavior.Restrict);
            });

            modelBuilder.Entity<FinanceAccount>(entity =>
            {
                entity.Property(a => a.Name).IsRequired().HasMaxLength(120);
                entity.Property(a => a.AccountHolderName).IsRequired().HasMaxLength(150);
                entity.Property(a => a.BankOrWalletName).HasMaxLength(150);
                entity.Property(a => a.Description).HasMaxLength(1000);
                entity.Property(a => a.OpeningBalance).HasColumnType("decimal(18,2)");
                entity.Property(a => a.RowVersion).IsRowVersion();

                entity.HasIndex(a => a.Name).IsUnique();
                entity.HasIndex(a => new { a.IsActive, a.Type });
                entity.HasIndex(a => a.AccountHolderName);
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

                entity.ToTable(t => t.HasCheckConstraint(
                    "CK_FinanceAttachments_ExactlyOneOwner",
                    "([ManualRevenueId] IS NOT NULL AND [ExpenseId] IS NULL) OR ([ManualRevenueId] IS NULL AND [ExpenseId] IS NOT NULL)"));

                entity.HasOne(a => a.ManualRevenue)
                      .WithOne(r => r.Attachment)
                      .HasForeignKey<FinanceAttachment>(a => a.ManualRevenueId)
                      .OnDelete(DeleteBehavior.Cascade);

                entity.HasOne(a => a.Expense)
                      .WithOne(e => e.Attachment)
                      .HasForeignKey<FinanceAttachment>(a => a.ExpenseId)
                      .OnDelete(DeleteBehavior.Cascade);
            });

            ConfigureLeadManagement(modelBuilder);
            ConfigureNotifications(modelBuilder);
        }

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

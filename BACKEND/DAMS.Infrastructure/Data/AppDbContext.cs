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

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            modelBuilder.Entity<Role>().HasData(
                new Role { RoleId = 1, Role_name = "Admin" },
                new Role { RoleId = 2, Role_name = "Client" }
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

                entity.HasIndex(b => b.BookingReference).IsUnique();
                entity.HasIndex(b => b.CustomerId);
                entity.HasIndex(b => b.UnitId);
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
                entity.HasIndex(s => new { s.EmployeeId, s.PayYear, s.PayMonth });
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
                entity.HasIndex(br => new { br.UnitId, br.Status });

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

                entity.HasOne(e => e.Project)
                      .WithMany()
                      .HasForeignKey(e => e.ProjectId)
                      .OnDelete(DeleteBehavior.SetNull);
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

                entity.HasOne(r => r.Project)
                      .WithMany()
                      .HasForeignKey(r => r.ProjectId)
                      .OnDelete(DeleteBehavior.SetNull);
            });
        }
    }
}

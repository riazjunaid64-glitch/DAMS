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
            });

            modelBuilder.Entity<Unit>(entity =>
            {
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

            modelBuilder.Entity<Booking>(entity =>
            {
                entity.Property(b => b.UnitPriceAtBooking).HasColumnType("decimal(18,2)");
                entity.Property(b => b.DownPaymentAmount).HasColumnType("decimal(18,2)");
                entity.Property(b => b.Status).HasConversion<int>();
                entity.HasIndex(b => b.ClientId);
                entity.HasIndex(b => b.UnitId);
                entity.HasOne(b => b.Client)
                      .WithMany(u => u.Bookings)
                      .HasForeignKey(b => b.ClientId)
                      .OnDelete(DeleteBehavior.Restrict);
                entity.HasOne(b => b.Unit)
                      .WithMany(u => u.Bookings)
                      .HasForeignKey(b => b.UnitId)
                      .OnDelete(DeleteBehavior.Restrict);
            });

            modelBuilder.Entity<Installment>(entity =>
            {
                entity.Property(i => i.Amount).HasColumnType("decimal(18,2)");
                entity.Property(i => i.Status).HasConversion<int>();
                entity.HasIndex(i => new { i.BookingId, i.SequenceNumber }).IsUnique();
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
                entity.Property(p => p.PaymentReference).HasMaxLength(500);
                entity.HasIndex(p => p.BookingId);
                entity.HasIndex(p => p.InstallmentId);
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
                entity.Property(e => e.Phone).IsRequired().HasMaxLength(20);
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
        }
    }
}

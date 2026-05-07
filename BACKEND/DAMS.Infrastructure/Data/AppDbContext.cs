using Microsoft.EntityFrameworkCore;
using DAMS.Domain.Entities;

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
});     modelBuilder.Entity<Unit>(entity =>
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

    entity.Property(u => u.Status)
          .HasConversion<string>();

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

    entity.HasOne(um => um.Unit)
          .WithMany(u => u.MediaFiles)
          .HasForeignKey(um => um.UnitId)
          .OnDelete(DeleteBehavior.Cascade);
});

}


    }
     

        
    
}

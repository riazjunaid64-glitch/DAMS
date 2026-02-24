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



          protected override void OnModelCreating(ModelBuilder modelBuilder)
{
    base.OnModelCreating(modelBuilder);
    
    modelBuilder.Entity<Role>().HasData(
        new Role { RoleId = 1, Role_name = "Admin" },
        new Role { RoleId = 2, Role_name = "Client" }
    );
}


    }
     

        
    
}
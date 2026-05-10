namespace  DAMS.Domain.Entities
{
    public class User
    {
        public int UserId { get; set; }

        // Foreign Key to Role table
         public int RoleId { get; set; }
         public Role Role { get; set; } = null!;
        public string FullName { get; set; } = null!;

        public string Email { get; set; } = null!;
        public string Password { get; set; } = null!;

        public string? RefreshToken { get; set; }
        public DateTime? RefreshTokenExpiresAt { get; set; }

        public ICollection<Booking> Bookings { get; set; } = new List<Booking>();
    }
}

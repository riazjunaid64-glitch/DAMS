using DAMS.Domain.Entities;
using DAMS.Domain.Enums;
using DAMS.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Xunit;

namespace DAMS.Application.Tests;

/// <summary>
/// The mapping half of the staff invitation foundation. Nullability, uniqueness and delete
/// behaviour are asserted against the model rather than a live database, so they hold on
/// every provider; the migration itself is proved against real SQL Server in
/// <see cref="SqlServerProductionInvariantTests"/>.
/// </summary>
public sealed class StaffInvitationPersistenceTests
{
    private static AppDbContext Context() => new(new DbContextOptionsBuilder<AppDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    [Fact]
    public void Default_account_status_is_Active_so_no_default_can_strand_an_existing_login()
    {
        // Active is deliberately 0: the CLR default, the column default and the enum's first
        // member all have to agree, or existing logins become un-activated on migration.
        Assert.Equal(0, (int)UserAccountStatus.Active);
        Assert.Equal(UserAccountStatus.Active, new User().AccountStatus);
        Assert.Equal(UserAccountStatus.Active, default(UserAccountStatus));
    }

    [Fact]
    public void Password_is_optional_so_an_invited_login_can_exist_before_it_has_one()
    {
        using var db = Context();
        var password = db.Model.FindEntityType(typeof(User))!.FindProperty(nameof(User.Password))!;

        Assert.True(password.IsNullable);
    }

    [Fact]
    public void Invitation_stores_only_a_required_unique_token_hash()
    {
        using var db = Context();
        var entity = db.Model.FindEntityType(typeof(StaffInvitation))!;
        var tokenHash = entity.FindProperty(nameof(StaffInvitation.TokenHash))!;

        Assert.False(tokenHash.IsNullable);
        Assert.Equal(128, tokenHash.GetMaxLength());
        Assert.Contains(entity.GetIndexes(),
            i => i.IsUnique && i.Properties.Single().Name == nameof(StaffInvitation.TokenHash));

        // Nothing on the entity may hold the emailed token itself.
        Assert.DoesNotContain(entity.GetProperties(),
            p => p.Name.Contains("Token", StringComparison.OrdinalIgnoreCase)
                 && p.Name != nameof(StaffInvitation.TokenHash));
    }

    [Fact]
    public void Both_user_relationships_exist_and_neither_cascades_the_grant_record_away()
    {
        using var db = Context();
        var entity = db.Model.FindEntityType(typeof(StaffInvitation))!;
        var foreignKeys = entity.GetForeignKeys().ToList();

        // Two foreign keys into Users: cascade on either would give SQL Server two delete
        // paths into the same table, and would erase who granted access.
        Assert.Equal(2, foreignKeys.Count);
        Assert.All(foreignKeys, fk =>
        {
            Assert.Equal(typeof(User), fk.PrincipalEntityType.ClrType);
            Assert.Equal(DeleteBehavior.Restrict, fk.DeleteBehavior);
        });
        Assert.Contains(foreignKeys, fk => fk.Properties.Single().Name == nameof(StaffInvitation.UserId));
        Assert.Contains(foreignKeys, fk => fk.Properties.Single().Name == nameof(StaffInvitation.InvitedByUserId));
    }

    [Fact]
    public void Lifecycle_timestamps_are_nullable_until_the_state_they_record_happens()
    {
        using var db = Context();
        var entity = db.Model.FindEntityType(typeof(StaffInvitation))!;

        Assert.False(entity.FindProperty(nameof(StaffInvitation.CreatedAt))!.IsNullable);
        Assert.False(entity.FindProperty(nameof(StaffInvitation.ExpiresAt))!.IsNullable);
        Assert.True(entity.FindProperty(nameof(StaffInvitation.AcceptedAt))!.IsNullable);
        Assert.True(entity.FindProperty(nameof(StaffInvitation.RevokedAt))!.IsNullable);
    }

    [Fact]
    public async Task An_invited_user_round_trips_with_no_password_and_its_invitation()
    {
        using var db = Context();
        var admin = new User
        {
            RoleId = 1, FullName = "Admin", Email = "admin@dams.test",
            Password = "hashed", AccountStatus = UserAccountStatus.Active
        };
        var invited = new User
        {
            RoleId = 4, FullName = "Invited Sales", Email = "invited@dams.test",
            Password = null, AccountStatus = UserAccountStatus.Invited
        };
        db.Users.AddRange(admin, invited);
        await db.SaveChangesAsync();

        db.StaffInvitations.Add(new StaffInvitation
        {
            UserId = invited.UserId,
            InvitedByUserId = admin.UserId,
            TokenHash = "a-sha256-hash",
            CreatedAt = DateTime.UtcNow,
            ExpiresAt = DateTime.UtcNow.AddDays(3)
        });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var invitation = await db.StaffInvitations
            .Include(i => i.User).Include(i => i.InvitedByUser).SingleAsync();

        Assert.Null(invitation.User.Password);
        Assert.Equal(UserAccountStatus.Invited, invitation.User.AccountStatus);
        Assert.Equal("admin@dams.test", invitation.InvitedByUser.Email);
        Assert.Null(invitation.AcceptedAt);
        Assert.Null(invitation.RevokedAt);
    }
}

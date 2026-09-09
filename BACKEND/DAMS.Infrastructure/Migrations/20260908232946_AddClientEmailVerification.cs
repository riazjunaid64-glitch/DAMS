using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DAMS.Infrastructure.Migrations
{
    /// <summary>
    /// Makes email ownership something DAMS has proven rather than something it assumed.
    ///
    /// <para>
    /// The migration is written to refuse rather than guess. It will not merge two logins that
    /// differ only in case, it will not mark a historical account as verified because it happens
    /// to be old, and it will not rebuild a single <c>Customer.UserId</c> from a matching email
    /// address. Where the data is ambiguous it stops with a message naming what a human has to
    /// decide, because every one of those shortcuts silently grants somebody access to another
    /// person's financial records.
    /// </para>
    ///
    /// <para>
    /// Nothing here touches Customers, Bookings, Payments, Installments or any finance table. The
    /// only existing rows it writes to are in Users, and only their status, session and the two
    /// new identity columns.
    /// </para>
    /// </summary>
    public partial class AddClientEmailVerification : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // ── 1. The canonical login-identity columns ─────────────────────────────────
            migrationBuilder.AddColumn<DateTime>(
                name: "EmailVerifiedAt",
                table: "Users",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "NormalizedEmail",
                table: "Users",
                type: "nvarchar(256)",
                maxLength: 256,
                nullable: true);

            // Filled under exactly the policy DAMS.Domain.Identity.EmailIdentity applies: trim,
            // then fold case upward. Nothing else — no +tag stripping, no dot removal. Those are
            // provider-specific conventions, and applying them would declare two addresses the
            // same identity when the mail server that owns them says otherwise.
            migrationBuilder.Sql(@"
UPDATE [Users]
SET [NormalizedEmail] = UPPER(LTRIM(RTRIM([Email])))
WHERE [Email] IS NOT NULL AND LTRIM(RTRIM([Email])) <> N'';
");

            // ── 2. Preflight: refuse to choose between duplicate identities ─────────────
            //
            // SQL Server's default collation is case-insensitive, so "Ali@example.com" and
            // "ali@example.com" may already sit in Users as two separate logins that the old
            // application-level check never caught. The unique index below would fail on them with
            // a constraint violation naming nothing useful, so this runs first and says which
            // addresses are involved. The resolution — which account is the real one, and what
            // happens to the other's data — is a business decision, and not one a migration may
            // take. Nothing is deleted, merged or renamed automatically.
            migrationBuilder.Sql(@"
DECLARE @duplicates NVARCHAR(MAX);

SELECT @duplicates = STRING_AGG(CAST([NormalizedEmail] AS NVARCHAR(MAX)), N', ')
FROM (
    SELECT [NormalizedEmail]
    FROM [Users]
    WHERE [NormalizedEmail] IS NOT NULL
    GROUP BY [NormalizedEmail]
    HAVING COUNT(*) > 1
) AS d;

IF @duplicates IS NOT NULL
BEGIN
    DECLARE @message NVARCHAR(2048) =
        N'Cannot apply AddClientEmailVerification: more than one login shares the same email address under DAMS''s comparison policy (trimmed, case-insensitive). '
        + N'Affected addresses: ' + @duplicates + N'. '
        + N'Decide which account is authoritative for each address and remove or re-address the others through the application, then re-run this migration. '
        + N'This migration will not merge, delete or rename accounts, because doing so would move one person''s bookings and payment history to another login.';
    THROW 51001, @message, 1;
END
");

            // Only now, with the column filled and proven unambiguous, is the invariant handed to
            // the storage engine. This index — not a read-then-write in application code — is what
            // makes two simultaneous registrations for one address produce one identity.
            migrationBuilder.CreateIndex(
                name: "IX_Users_NormalizedEmail",
                table: "Users",
                column: "NormalizedEmail",
                unique: true,
                filter: "[NormalizedEmail] IS NOT NULL");

            // ── 3. The verification credential ──────────────────────────────────────────
            migrationBuilder.CreateTable(
                name: "ClientEmailVerifications",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UserId = table.Column<int>(type: "int", nullable: false),
                    TokenHash = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    VerifiedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    RevokedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ClientEmailVerifications", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ClientEmailVerifications_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "UserId",
                        onDelete: ReferentialAction.Restrict);
                });

            // ── 4. The ownership audit trail ────────────────────────────────────────────
            migrationBuilder.CreateTable(
                name: "CustomerAccountLinkAudits",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CustomerId = table.Column<int>(type: "int", nullable: false),
                    PreviousUserId = table.Column<int>(type: "int", nullable: true),
                    AttemptedUserId = table.Column<int>(type: "int", nullable: true),
                    ResultingUserId = table.Column<int>(type: "int", nullable: true),
                    Action = table.Column<int>(type: "int", nullable: false),
                    PerformedByUserId = table.Column<int>(type: "int", nullable: true),
                    BookingRequestId = table.Column<int>(type: "int", nullable: true),
                    Reason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    OccurredAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CustomerAccountLinkAudits", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CustomerAccountLinkAudits_Customers_CustomerId",
                        column: x => x.CustomerId,
                        principalTable: "Customers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ClientEmailVerifications_ExpiresAt",
                table: "ClientEmailVerifications",
                column: "ExpiresAt");

            migrationBuilder.CreateIndex(
                name: "IX_ClientEmailVerifications_TokenHash",
                table: "ClientEmailVerifications",
                column: "TokenHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ClientEmailVerifications_UserId_CreatedAt",
                table: "ClientEmailVerifications",
                columns: new[] { "UserId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_CustomerAccountLinkAudits_CustomerId_OccurredAt",
                table: "CustomerAccountLinkAudits",
                columns: new[] { "CustomerId", "OccurredAt" });

            // ── 5. The legacy client transition ─────────────────────────────────────────
            //
            // Every existing public client login moves to PendingEmailVerification (3) and keeps
            // EmailVerifiedAt NULL, because DAMS has genuinely never proven any of those
            // addresses. Marking them verified would be recording a fact that was never
            // established, and would leave in place precisely the accounts an attacker could have
            // opened by registering somebody else's address before this shipped.
            //
            // The password is left as it is. It cannot be used — login refuses any status other
            // than Active — and verification overwrites it with the one the mailbox owner chooses.
            // Deleting it here would gain nothing and lose the ability to reason about the row.
            //
            // Staff are untouched. Admin, Manager and Employee logins prove themselves through an
            // Admin-issued invitation and an employment record, which is a different proof of a
            // different thing; sweeping them into the client lifecycle would lock out the whole
            // company for no security gain. Client rows that are Invited or Disabled are also left
            // alone: their status already denies access, and overwriting it would lose why.
            migrationBuilder.Sql(@"
UPDATE u
SET u.[AccountStatus] = 3,
    u.[EmailVerifiedAt] = NULL
FROM [Users] u
INNER JOIN [Roles] r ON r.[RoleId] = u.[RoleId]
WHERE r.[Role_name] = N'Client'
  AND u.[AccountStatus] = 0;
");

            // Old client sessions die here, whatever status the account is in. A refresh token is
            // a standing permission to keep minting access tokens for fifteen days; without this,
            // every pre-existing client session would go on renewing itself straight past the
            // verification requirement this migration introduces.
            migrationBuilder.Sql(@"
UPDATE u
SET u.[RefreshToken] = NULL,
    u.[RefreshTokenExpiresAt] = NULL
FROM [Users] u
INNER JOIN [Roles] r ON r.[RoleId] = u.[RoleId]
WHERE r.[Role_name] = N'Client'
  AND u.[RefreshToken] IS NOT NULL;
");

            // ── 6. Report, do not repair ────────────────────────────────────────────────
            //
            // Existing Customer.UserId links are left exactly as they are. They are the only record
            // DAMS has of who owns what, and rebuilding them from matching email addresses — the
            // tempting shortcut, and the one this work exists to remove — would hand every customer
            // record to whichever login happens to share its address.
            //
            // What is worth surfacing is the opposite: customers whose email matches a login they
            // are NOT linked to. Those are the records that used to be reachable through the email
            // fallback and are about to stop being. Nothing is changed; the counts are printed so
            // whoever runs the deployment knows how much support traffic to expect and can work
            // through the claim flow deliberately.
            migrationBuilder.Sql(@"
DECLARE @unlinked INT, @mismatched INT;

SELECT @unlinked = COUNT(*)
FROM [Customers] c
INNER JOIN [Users] u ON u.[NormalizedEmail] = UPPER(LTRIM(RTRIM(c.[Email])))
WHERE c.[Email] IS NOT NULL AND c.[UserId] IS NULL;

SELECT @mismatched = COUNT(*)
FROM [Customers] c
INNER JOIN [Users] u ON u.[NormalizedEmail] = UPPER(LTRIM(RTRIM(c.[Email])))
WHERE c.[Email] IS NOT NULL AND c.[UserId] IS NOT NULL AND c.[UserId] <> u.[UserId];

PRINT N'AddClientEmailVerification: ' + CAST(ISNULL(@unlinked, 0) AS NVARCHAR(20))
    + N' customer record(s) share an email address with a login they are not linked to, and '
    + CAST(ISNULL(@mismatched, 0) AS NVARCHAR(20))
    + N' are linked to a different login than the address suggests. '
    + N'None have been changed. Portal access for these customers now requires an explicit, audited account link.';
");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // The rollback deliberately does NOT put clients back to Active.
            //
            // Reverting the schema is one thing; silently re-opening every client account that had
            // not verified — and with it the email-matching authorization that made this migration
            // necessary — is another. An operator rolling back for an unrelated reason would not
            // expect that, and would have no way to see it had happened. So the accounts stay
            // pending, and the message says what is left to decide.
            migrationBuilder.Sql(@"
IF EXISTS (SELECT 1 FROM [Users] WHERE [AccountStatus] = 3)
    PRINT N'AddClientEmailVerification rollback: client logins left in PendingEmailVerification are NOT being reactivated. Reactivate deliberately, per account, once you have decided how each address is to be verified.';
");

            migrationBuilder.DropTable(
                name: "ClientEmailVerifications");

            migrationBuilder.DropTable(
                name: "CustomerAccountLinkAudits");

            migrationBuilder.DropIndex(
                name: "IX_Users_NormalizedEmail",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "EmailVerifiedAt",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "NormalizedEmail",
                table: "Users");
        }
    }
}

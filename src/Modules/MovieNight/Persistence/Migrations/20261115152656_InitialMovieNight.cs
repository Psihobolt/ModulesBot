using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Modules.MovieNight.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialMovieNight : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "movienight_group_members",
                columns: table => new
                {
                    JoinSeq = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    GroupId = table.Column<Guid>(type: "TEXT", nullable: false),
                    UserId = table.Column<long>(type: "INTEGER", nullable: false),
                    JoinedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_movienight_group_members", x => x.JoinSeq);
                });

            migrationBuilder.CreateTable(
                name: "movienight_groups",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    InviteCode = table.Column<string>(type: "TEXT", nullable: false),
                    OwnerId = table.Column<long>(type: "INTEGER", nullable: true),
                    CreatedBy = table.Column<long>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_movienight_groups", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "movienight_revoked_invite_codes",
                columns: table => new
                {
                    Code = table.Column<string>(type: "TEXT", nullable: false),
                    GroupId = table.Column<Guid>(type: "TEXT", nullable: false),
                    RevokedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_movienight_revoked_invite_codes", x => x.Code);
                });

            migrationBuilder.CreateIndex(
                name: "IX_movienight_group_members_GroupId_UserId",
                table: "movienight_group_members",
                columns: new[] { "GroupId", "UserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_movienight_group_members_UserId",
                table: "movienight_group_members",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_movienight_groups_InviteCode",
                table: "movienight_groups",
                column: "InviteCode",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "movienight_group_members");

            migrationBuilder.DropTable(
                name: "movienight_groups");

            migrationBuilder.DropTable(
                name: "movienight_revoked_invite_codes");
        }
    }
}

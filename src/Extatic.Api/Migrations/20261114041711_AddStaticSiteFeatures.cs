using System;
using Microsoft.EntityFrameworkCore.Migrations;
using NpgsqlTypes;

#nullable disable

namespace Extatic.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddStaticSiteFeatures : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "SourceSchemaId",
                table: "collections",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SourceSchemaVersion",
                table: "collections",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "DisabledAt",
                table: "app_users",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "EmailVerifiedAt",
                table: "app_users",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "app_user_auth_tokens",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    AppId = table.Column<Guid>(type: "uuid", nullable: false),
                    AppUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Purpose = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    TokenHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UsedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    RevokedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ReplacedByTokenId = table.Column<Guid>(type: "uuid", nullable: true),
                    FamilyId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    IpHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_app_user_auth_tokens", x => x.Id);
                    table.ForeignKey(
                        name: "FK_app_user_auth_tokens_app_users_AppUserId",
                        column: x => x.AppUserId,
                        principalTable: "app_users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_app_user_auth_tokens_apps_AppId",
                        column: x => x.AppId,
                        principalTable: "apps",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "app_user_credentials",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    AppId = table.Column<Guid>(type: "uuid", nullable: false),
                    AppUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    PasswordHash = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    FailedAttempts = table.Column<int>(type: "integer", nullable: false),
                    LockedUntil = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    PasswordChangedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_app_user_credentials", x => x.Id);
                    table.ForeignKey(
                        name: "FK_app_user_credentials_app_users_AppUserId",
                        column: x => x.AppUserId,
                        principalTable: "app_users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_app_user_credentials_apps_AppId",
                        column: x => x.AppId,
                        principalTable: "apps",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "auth_settings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    AppId = table.Column<Guid>(type: "uuid", nullable: false),
                    RegistrationEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    RequireEmailVerification = table.Column<bool>(type: "boolean", nullable: false),
                    PasswordMinLength = table.Column<int>(type: "integer", nullable: false),
                    AccessTokenMinutes = table.Column<int>(type: "integer", nullable: false),
                    RefreshTokenDays = table.Column<int>(type: "integer", nullable: false),
                    MaxFailedAttempts = table.Column<int>(type: "integer", nullable: false),
                    LockoutMinutes = table.Column<int>(type: "integer", nullable: false),
                    VerifyUrlTemplate = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    ResetUrlTemplate = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    AllowedEmailDomains = table.Column<string[]>(type: "text[]", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_auth_settings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_auth_settings_apps_AppId",
                        column: x => x.AppId,
                        principalTable: "apps",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "booking_calendars",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    AppId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Slug = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Timezone = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    NotifyEmails = table.Column<string[]>(type: "text[]", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_booking_calendars", x => x.Id);
                    table.ForeignKey(
                        name: "FK_booking_calendars_apps_AppId",
                        column: x => x.AppId,
                        principalTable: "apps",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "comment_pages",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    AppId = table.Column<Guid>(type: "uuid", nullable: false),
                    PageKey = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    IsClosed = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_comment_pages", x => x.Id);
                    table.ForeignKey(
                        name: "FK_comment_pages_apps_AppId",
                        column: x => x.AppId,
                        principalTable: "apps",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "comment_settings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    AppId = table.Column<Guid>(type: "uuid", nullable: false),
                    Enabled = table.Column<bool>(type: "boolean", nullable: false),
                    ModerationMode = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    AllowAnonymous = table.Column<bool>(type: "boolean", nullable: false),
                    RequireEmailForAnonymous = table.Column<bool>(type: "boolean", nullable: false),
                    MaxLength = table.Column<int>(type: "integer", nullable: false),
                    MaxDepth = table.Column<int>(type: "integer", nullable: false),
                    AutoCloseAfterDays = table.Column<int>(type: "integer", nullable: true),
                    BlockedTerms = table.Column<string[]>(type: "text[]", nullable: false),
                    BlockedTermAction = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    MaxLinks = table.Column<int>(type: "integer", nullable: false),
                    NotifyEmails = table.Column<string[]>(type: "text[]", nullable: false),
                    ExposeAvatarHash = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_comment_settings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_comment_settings_apps_AppId",
                        column: x => x.AppId,
                        principalTable: "apps",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "comments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    AppId = table.Column<Guid>(type: "uuid", nullable: false),
                    PageKey = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    PageUrl = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    PageTitle = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    ParentId = table.Column<Guid>(type: "uuid", nullable: true),
                    Depth = table.Column<int>(type: "integer", nullable: false),
                    AppUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    AuthorName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    AuthorEmail = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: true),
                    Body = table.Column<string>(type: "text", nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    IpHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    UserAgent = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    EditedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ModeratedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ModeratedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_comments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_comments_app_users_AppUserId",
                        column: x => x.AppUserId,
                        principalTable: "app_users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_comments_apps_AppId",
                        column: x => x.AppId,
                        principalTable: "apps",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_comments_comments_ParentId",
                        column: x => x.ParentId,
                        principalTable: "comments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_comments_users_ModeratedByUserId",
                        column: x => x.ModeratedByUserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "donation_campaigns",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    AppId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Slug = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Description = table.Column<string>(type: "character varying(5000)", maxLength: 5000, nullable: true),
                    Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    GoalAmountMinor = table.Column<long>(type: "bigint", nullable: true),
                    SuggestedAmountsMinor = table.Column<long[]>(type: "bigint[]", nullable: false),
                    MinAmountMinor = table.Column<long>(type: "bigint", nullable: false),
                    MaxAmountMinor = table.Column<long>(type: "bigint", nullable: false),
                    AllowCustomAmount = table.Column<bool>(type: "boolean", nullable: false),
                    AllowRecurring = table.Column<bool>(type: "boolean", nullable: false),
                    SuccessUrl = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    CancelUrl = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    ShowDonorList = table.Column<bool>(type: "boolean", nullable: false),
                    NotifyEmails = table.Column<string[]>(type: "text[]", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    StartsAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    EndsAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_donation_campaigns", x => x.Id);
                    table.ForeignKey(
                        name: "FK_donation_campaigns_apps_AppId",
                        column: x => x.AppId,
                        principalTable: "apps",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "events",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    AppId = table.Column<Guid>(type: "uuid", nullable: false),
                    Title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Slug = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Description = table.Column<string>(type: "character varying(10000)", maxLength: 10000, nullable: true),
                    Location = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    OnlineUrl = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    StartsAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    EndsAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Timezone = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Capacity = table.Column<int>(type: "integer", nullable: true),
                    WaitlistEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    WaitlistCapacity = table.Column<int>(type: "integer", nullable: true),
                    MaxSeatsPerRegistration = table.Column<int>(type: "integer", nullable: false, defaultValue: 1),
                    RegistrationOpensAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    RegistrationClosesAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    RegistrationSchema = table.Column<string>(type: "jsonb", nullable: true),
                    RequireAppUser = table.Column<bool>(type: "boolean", nullable: false),
                    ManageUrlTemplate = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    NotifyEmails = table.Column<string[]>(type: "text[]", nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_events", x => x.Id);
                    table.ForeignKey(
                        name: "FK_events_apps_AppId",
                        column: x => x.AppId,
                        principalTable: "apps",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "forms",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    AppId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Slug = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Schema = table.Column<string>(type: "jsonb", nullable: true),
                    NotificationEmails = table.Column<string[]>(type: "text[]", nullable: false),
                    SuccessRedirectUrl = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    HoneypotField = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    StoreSubmissions = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    RequireAppUser = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_forms", x => x.Id);
                    table.ForeignKey(
                        name: "FK_forms_apps_AppId",
                        column: x => x.AppId,
                        principalTable: "apps",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "library_schemas",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    Scope = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    AppId = table.Column<Guid>(type: "uuid", nullable: true),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Slug = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Description = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    Category = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Tags = table.Column<string[]>(type: "text[]", nullable: false),
                    Visibility = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    Schema = table.Column<string>(type: "jsonb", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    IsArchived = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    UpdatedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_library_schemas", x => x.Id);
                    table.CheckConstraint("ck_library_schemas_scope", "(\"Scope\" = 'Global' AND \"AppId\" IS NULL AND \"Visibility\" IS NOT NULL) OR (\"Scope\" = 'App' AND \"AppId\" IS NOT NULL AND \"Visibility\" IS NULL)");
                    table.ForeignKey(
                        name: "FK_library_schemas_apps_AppId",
                        column: x => x.AppId,
                        principalTable: "apps",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "member_settings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    AppId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProfileSchema = table.Column<string>(type: "jsonb", nullable: true),
                    DirectoryEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    DirectoryFields = table.Column<string[]>(type: "text[]", nullable: false),
                    MemberNumberPrefix = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    NextMemberNumber = table.Column<long>(type: "bigint", nullable: false),
                    NotifyEmails = table.Column<string[]>(type: "text[]", nullable: false),
                    RenewalReminderDays = table.Column<int[]>(type: "integer[]", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_member_settings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_member_settings_apps_AppId",
                        column: x => x.AppId,
                        principalTable: "apps",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "membership_plans",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    AppId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Slug = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Description = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    PriceLabel = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    DurationDays = table.Column<int>(type: "integer", nullable: true),
                    RequiresApproval = table.Column<bool>(type: "boolean", nullable: false),
                    SelfSignupEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    MaxMembers = table.Column<int>(type: "integer", nullable: true),
                    Benefits = table.Column<string>(type: "jsonb", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_membership_plans", x => x.Id);
                    table.ForeignKey(
                        name: "FK_membership_plans_apps_AppId",
                        column: x => x.AppId,
                        principalTable: "apps",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "payment_provider_configs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    AppId = table.Column<Guid>(type: "uuid", nullable: false),
                    Provider = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    PublishableKey = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    SecretKeyProtected = table.Column<string>(type: "text", nullable: false),
                    SecretKeyLast4 = table.Column<string>(type: "character varying(4)", maxLength: 4, nullable: false),
                    WebhookSecretProtected = table.Column<string>(type: "text", nullable: true),
                    WebhookSecretLast4 = table.Column<string>(type: "character varying(4)", maxLength: 4, nullable: true),
                    IsLiveMode = table.Column<bool>(type: "boolean", nullable: false),
                    IsEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_payment_provider_configs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_payment_provider_configs_apps_AppId",
                        column: x => x.AppId,
                        principalTable: "apps",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "payment_webhook_events",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    AppId = table.Column<Guid>(type: "uuid", nullable: false),
                    Provider = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    ProviderEventId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    Type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    ProcessedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_payment_webhook_events", x => x.Id);
                    table.ForeignKey(
                        name: "FK_payment_webhook_events_apps_AppId",
                        column: x => x.AppId,
                        principalTable: "apps",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "search_collection_configs",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    app_id = table.Column<Guid>(type: "uuid", nullable: false),
                    collection_id = table.Column<Guid>(type: "uuid", nullable: false),
                    enabled = table.Column<bool>(type: "boolean", nullable: false),
                    title_pointer = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    body_pointers = table.Column<string[]>(type: "text[]", nullable: false),
                    tags_pointer = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    url_template = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    section = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    language = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_search_collection_configs", x => x.id);
                    table.ForeignKey(
                        name: "FK_search_collection_configs_apps_app_id",
                        column: x => x.app_id,
                        principalTable: "apps",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_search_collection_configs_collections_collection_id",
                        column: x => x.collection_id,
                        principalTable: "collections",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "search_documents",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    app_id = table.Column<Guid>(type: "uuid", nullable: false),
                    source = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    external_id = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    collection_id = table.Column<Guid>(type: "uuid", nullable: true),
                    item_id = table.Column<Guid>(type: "uuid", nullable: true),
                    url = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    title = table.Column<string>(type: "text", nullable: false),
                    body = table.Column<string>(type: "text", nullable: false),
                    summary = table.Column<string>(type: "text", nullable: true),
                    tags = table.Column<string[]>(type: "text[]", nullable: false),
                    section = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    language = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    metadata = table.Column<string>(type: "jsonb", nullable: false),
                    content_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    published_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    indexed_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    search_vector = table.Column<NpgsqlTsVector>(type: "tsvector", nullable: false, defaultValueSql: "''::tsvector"),
                    title_vector = table.Column<NpgsqlTsVector>(type: "tsvector", nullable: true)
                        .Annotation("Npgsql:TsVectorConfig", "simple")
                        .Annotation("Npgsql:TsVectorProperties", new[] { "title" }),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_search_documents", x => x.id);
                    table.ForeignKey(
                        name: "FK_search_documents_apps_app_id",
                        column: x => x.app_id,
                        principalTable: "apps",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_search_documents_collections_collection_id",
                        column: x => x.collection_id,
                        principalTable: "collections",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_search_documents_items_item_id",
                        column: x => x.item_id,
                        principalTable: "items",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "search_query_logs",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    app_id = table.Column<Guid>(type: "uuid", nullable: false),
                    query = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    result_count = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_search_query_logs", x => x.id);
                    table.ForeignKey(
                        name: "FK_search_query_logs_apps_app_id",
                        column: x => x.app_id,
                        principalTable: "apps",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "search_reindex_jobs",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    app_id = table.Column<Guid>(type: "uuid", nullable: false),
                    collection_id = table.Column<Guid>(type: "uuid", nullable: true),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    processed = table.Column<int>(type: "integer", nullable: false),
                    total = table.Column<int>(type: "integer", nullable: false),
                    error = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    attempts = table.Column<int>(type: "integer", nullable: false),
                    started_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    completed_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    lease_expires_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_search_reindex_jobs", x => x.id);
                    table.ForeignKey(
                        name: "FK_search_reindex_jobs_apps_app_id",
                        column: x => x.app_id,
                        principalTable: "apps",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_search_reindex_jobs_collections_collection_id",
                        column: x => x.collection_id,
                        principalTable: "collections",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "search_settings",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    app_id = table.Column<Guid>(type: "uuid", nullable: false),
                    enabled = table.Column<bool>(type: "boolean", nullable: false),
                    default_language = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    max_documents = table.Column<int>(type: "integer", nullable: false),
                    public_search_enabled = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_search_settings", x => x.id);
                    table.ForeignKey(
                        name: "FK_search_settings_apps_app_id",
                        column: x => x.app_id,
                        principalTable: "apps",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "appointment_types",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    AppId = table.Column<Guid>(type: "uuid", nullable: false),
                    CalendarId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Slug = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Description = table.Column<string>(type: "character varying(5000)", maxLength: 5000, nullable: true),
                    DurationMinutes = table.Column<int>(type: "integer", nullable: false),
                    BufferBeforeMinutes = table.Column<int>(type: "integer", nullable: false),
                    BufferAfterMinutes = table.Column<int>(type: "integer", nullable: false),
                    SlotIntervalMinutes = table.Column<int>(type: "integer", nullable: false),
                    MinNoticeMinutes = table.Column<int>(type: "integer", nullable: false),
                    MaxAdvanceDays = table.Column<int>(type: "integer", nullable: false),
                    MaxPerDay = table.Column<int>(type: "integer", nullable: true),
                    RequiresApproval = table.Column<bool>(type: "boolean", nullable: false),
                    IntakeSchema = table.Column<string>(type: "jsonb", nullable: true),
                    Location = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    MeetingUrl = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    ManageUrlTemplate = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_appointment_types", x => x.Id);
                    table.ForeignKey(
                        name: "FK_appointment_types_apps_AppId",
                        column: x => x.AppId,
                        principalTable: "apps",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_appointment_types_booking_calendars_CalendarId",
                        column: x => x.CalendarId,
                        principalTable: "booking_calendars",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "availability_overrides",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    AppId = table.Column<Guid>(type: "uuid", nullable: false),
                    CalendarId = table.Column<Guid>(type: "uuid", nullable: false),
                    Date = table.Column<DateOnly>(type: "date", nullable: false),
                    IsUnavailable = table.Column<bool>(type: "boolean", nullable: false),
                    StartTime = table.Column<TimeOnly>(type: "time without time zone", nullable: true),
                    EndTime = table.Column<TimeOnly>(type: "time without time zone", nullable: true),
                    Reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_availability_overrides", x => x.Id);
                    table.ForeignKey(
                        name: "FK_availability_overrides_apps_AppId",
                        column: x => x.AppId,
                        principalTable: "apps",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_availability_overrides_booking_calendars_CalendarId",
                        column: x => x.CalendarId,
                        principalTable: "booking_calendars",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "availability_rules",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    AppId = table.Column<Guid>(type: "uuid", nullable: false),
                    CalendarId = table.Column<Guid>(type: "uuid", nullable: false),
                    DayOfWeek = table.Column<int>(type: "integer", nullable: false),
                    StartTime = table.Column<TimeOnly>(type: "time without time zone", nullable: false),
                    EndTime = table.Column<TimeOnly>(type: "time without time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_availability_rules", x => x.Id);
                    table.ForeignKey(
                        name: "FK_availability_rules_apps_AppId",
                        column: x => x.AppId,
                        principalTable: "apps",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_availability_rules_booking_calendars_CalendarId",
                        column: x => x.CalendarId,
                        principalTable: "booking_calendars",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "donations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    AppId = table.Column<Guid>(type: "uuid", nullable: false),
                    CampaignId = table.Column<Guid>(type: "uuid", nullable: true),
                    AppUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    AmountMinor = table.Column<long>(type: "bigint", nullable: false),
                    Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    Interval = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    DonorName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    DonorEmail = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: true),
                    IsAnonymous = table.Column<bool>(type: "boolean", nullable: false),
                    Message = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    Provider = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    ProviderSessionId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    ProviderPaymentId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    ProviderSubscriptionId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    RefundedAmountMinor = table.Column<long>(type: "bigint", nullable: false),
                    CompletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    IpHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_donations", x => x.Id);
                    table.CheckConstraint("CK_donations_amount_positive", "\"AmountMinor\" > 0");
                    table.CheckConstraint("CK_donations_refund_range", "\"RefundedAmountMinor\" >= 0 AND \"RefundedAmountMinor\" <= \"AmountMinor\"");
                    table.ForeignKey(
                        name: "FK_donations_app_users_AppUserId",
                        column: x => x.AppUserId,
                        principalTable: "app_users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_donations_apps_AppId",
                        column: x => x.AppId,
                        principalTable: "apps",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_donations_donation_campaigns_CampaignId",
                        column: x => x.CampaignId,
                        principalTable: "donation_campaigns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "event_registrations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    AppId = table.Column<Guid>(type: "uuid", nullable: false),
                    EventId = table.Column<Guid>(type: "uuid", nullable: false),
                    AppUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Email = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                    Seats = table.Column<int>(type: "integer", nullable: false),
                    Data = table.Column<string>(type: "jsonb", nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    ManageTokenHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CheckedInAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ConfirmedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CancelledAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    PromotedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_event_registrations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_event_registrations_app_users_AppUserId",
                        column: x => x.AppUserId,
                        principalTable: "app_users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_event_registrations_apps_AppId",
                        column: x => x.AppId,
                        principalTable: "apps",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_event_registrations_events_EventId",
                        column: x => x.EventId,
                        principalTable: "events",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "form_submissions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    AppId = table.Column<Guid>(type: "uuid", nullable: false),
                    FormId = table.Column<Guid>(type: "uuid", nullable: false),
                    Data = table.Column<string>(type: "jsonb", nullable: false),
                    AppUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    IpHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    UserAgent = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    Referrer = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_form_submissions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_form_submissions_app_users_AppUserId",
                        column: x => x.AppUserId,
                        principalTable: "app_users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_form_submissions_apps_AppId",
                        column: x => x.AppId,
                        principalTable: "apps",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_form_submissions_forms_FormId",
                        column: x => x.FormId,
                        principalTable: "forms",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "library_schema_versions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    LibrarySchemaId = table.Column<Guid>(type: "uuid", nullable: false),
                    AppId = table.Column<Guid>(type: "uuid", nullable: true),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    Schema = table.Column<string>(type: "jsonb", nullable: false),
                    ChangeNote = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_library_schema_versions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_library_schema_versions_apps_AppId",
                        column: x => x.AppId,
                        principalTable: "apps",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_library_schema_versions_library_schemas_LibrarySchemaId",
                        column: x => x.LibrarySchemaId,
                        principalTable: "library_schemas",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "memberships",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    AppId = table.Column<Guid>(type: "uuid", nullable: false),
                    AppUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    PlanId = table.Column<Guid>(type: "uuid", nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    MemberNumber = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    Profile = table.Column<string>(type: "jsonb", nullable: false),
                    StartsAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ApprovedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ApprovedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    CancelledAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Notes = table.Column<string>(type: "character varying(10000)", maxLength: 10000, nullable: true),
                    ShowInDirectory = table.Column<bool>(type: "boolean", nullable: false),
                    RemindersSent = table.Column<int[]>(type: "integer[]", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_memberships", x => x.Id);
                    table.ForeignKey(
                        name: "FK_memberships_app_users_AppUserId",
                        column: x => x.AppUserId,
                        principalTable: "app_users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_memberships_apps_AppId",
                        column: x => x.AppId,
                        principalTable: "apps",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_memberships_membership_plans_PlanId",
                        column: x => x.PlanId,
                        principalTable: "membership_plans",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "appointments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    AppId = table.Column<Guid>(type: "uuid", nullable: false),
                    CalendarId = table.Column<Guid>(type: "uuid", nullable: false),
                    AppointmentTypeId = table.Column<Guid>(type: "uuid", nullable: false),
                    AppUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    CustomerName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    CustomerEmail = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                    CustomerPhone = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    CustomerTimezone = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    StartsAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    EndsAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    BlockStartsAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    BlockEndsAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Data = table.Column<string>(type: "jsonb", nullable: false),
                    Notes = table.Column<string>(type: "character varying(5000)", maxLength: 5000, nullable: true),
                    ManageTokenHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CancelledAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CancelReason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    ReminderSentAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_appointments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_appointments_app_users_AppUserId",
                        column: x => x.AppUserId,
                        principalTable: "app_users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_appointments_appointment_types_AppointmentTypeId",
                        column: x => x.AppointmentTypeId,
                        principalTable: "appointment_types",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_appointments_apps_AppId",
                        column: x => x.AppId,
                        principalTable: "apps",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_appointments_booking_calendars_CalendarId",
                        column: x => x.CalendarId,
                        principalTable: "booking_calendars",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "membership_events",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    AppId = table.Column<Guid>(type: "uuid", nullable: false),
                    MembershipId = table.Column<Guid>(type: "uuid", nullable: false),
                    Type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    FromStatus = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    ToStatus = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    ActorUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    ActorAppUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    Detail = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_membership_events", x => x.Id);
                    table.ForeignKey(
                        name: "FK_membership_events_apps_AppId",
                        column: x => x.AppId,
                        principalTable: "apps",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_membership_events_memberships_MembershipId",
                        column: x => x.MembershipId,
                        principalTable: "memberships",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_collections_SourceSchemaId",
                table: "collections",
                column: "SourceSchemaId");

            migrationBuilder.CreateIndex(
                name: "IX_app_user_auth_tokens_AppId_AppUserId_Purpose",
                table: "app_user_auth_tokens",
                columns: new[] { "AppId", "AppUserId", "Purpose" });

            migrationBuilder.CreateIndex(
                name: "IX_app_user_auth_tokens_AppUserId",
                table: "app_user_auth_tokens",
                column: "AppUserId");

            migrationBuilder.CreateIndex(
                name: "IX_app_user_auth_tokens_ExpiresAt",
                table: "app_user_auth_tokens",
                column: "ExpiresAt");

            migrationBuilder.CreateIndex(
                name: "IX_app_user_auth_tokens_FamilyId",
                table: "app_user_auth_tokens",
                column: "FamilyId");

            migrationBuilder.CreateIndex(
                name: "IX_app_user_auth_tokens_TokenHash",
                table: "app_user_auth_tokens",
                column: "TokenHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_app_user_credentials_AppId",
                table: "app_user_credentials",
                column: "AppId");

            migrationBuilder.CreateIndex(
                name: "IX_app_user_credentials_AppUserId",
                table: "app_user_credentials",
                column: "AppUserId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_appointment_types_AppId_Slug",
                table: "appointment_types",
                columns: new[] { "AppId", "Slug" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_appointment_types_CalendarId",
                table: "appointment_types",
                column: "CalendarId");

            migrationBuilder.CreateIndex(
                name: "IX_appointments_AppId_StartsAt",
                table: "appointments",
                columns: new[] { "AppId", "StartsAt" });

            migrationBuilder.CreateIndex(
                name: "IX_appointments_AppointmentTypeId",
                table: "appointments",
                column: "AppointmentTypeId");

            migrationBuilder.CreateIndex(
                name: "IX_appointments_AppUserId",
                table: "appointments",
                column: "AppUserId");

            migrationBuilder.CreateIndex(
                name: "IX_appointments_CalendarId_BlockStartsAt",
                table: "appointments",
                columns: new[] { "CalendarId", "BlockStartsAt" });

            migrationBuilder.CreateIndex(
                name: "IX_appointments_ManageTokenHash",
                table: "appointments",
                column: "ManageTokenHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_appointments_reminder_due",
                table: "appointments",
                column: "StartsAt",
                filter: "\"ReminderSentAt\" IS NULL AND \"Status\" = 'Confirmed'");

            migrationBuilder.CreateIndex(
                name: "IX_auth_settings_AppId",
                table: "auth_settings",
                column: "AppId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_availability_overrides_AppId",
                table: "availability_overrides",
                column: "AppId");

            migrationBuilder.CreateIndex(
                name: "IX_availability_overrides_CalendarId_Date",
                table: "availability_overrides",
                columns: new[] { "CalendarId", "Date" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_availability_rules_AppId",
                table: "availability_rules",
                column: "AppId");

            migrationBuilder.CreateIndex(
                name: "IX_availability_rules_CalendarId_DayOfWeek",
                table: "availability_rules",
                columns: new[] { "CalendarId", "DayOfWeek" });

            migrationBuilder.CreateIndex(
                name: "IX_booking_calendars_AppId_Slug",
                table: "booking_calendars",
                columns: new[] { "AppId", "Slug" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_comment_pages_AppId_PageKey",
                table: "comment_pages",
                columns: new[] { "AppId", "PageKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_comment_settings_AppId",
                table: "comment_settings",
                column: "AppId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_comments_AppId_PageKey_Status_CreatedAt",
                table: "comments",
                columns: new[] { "AppId", "PageKey", "Status", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_comments_AppId_Status_CreatedAt",
                table: "comments",
                columns: new[] { "AppId", "Status", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_comments_AppUserId",
                table: "comments",
                column: "AppUserId");

            migrationBuilder.CreateIndex(
                name: "IX_comments_ModeratedByUserId",
                table: "comments",
                column: "ModeratedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_comments_ParentId",
                table: "comments",
                column: "ParentId");

            migrationBuilder.CreateIndex(
                name: "IX_donation_campaigns_AppId_Slug",
                table: "donation_campaigns",
                columns: new[] { "AppId", "Slug" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_donations_AppId_CampaignId_Status",
                table: "donations",
                columns: new[] { "AppId", "CampaignId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_donations_AppId_CreatedAt",
                table: "donations",
                columns: new[] { "AppId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_donations_AppId_Provider_ProviderPaymentId",
                table: "donations",
                columns: new[] { "AppId", "Provider", "ProviderPaymentId" });

            migrationBuilder.CreateIndex(
                name: "IX_donations_AppUserId",
                table: "donations",
                column: "AppUserId");

            migrationBuilder.CreateIndex(
                name: "IX_donations_CampaignId",
                table: "donations",
                column: "CampaignId");

            migrationBuilder.CreateIndex(
                name: "IX_donations_Provider_ProviderSessionId",
                table: "donations",
                columns: new[] { "Provider", "ProviderSessionId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_event_registrations_AppId_AppUserId",
                table: "event_registrations",
                columns: new[] { "AppId", "AppUserId" });

            migrationBuilder.CreateIndex(
                name: "IX_event_registrations_AppUserId",
                table: "event_registrations",
                column: "AppUserId");

            migrationBuilder.CreateIndex(
                name: "IX_event_registrations_EventId_Email_active",
                table: "event_registrations",
                columns: new[] { "EventId", "Email" },
                unique: true,
                filter: "\"Status\" <> 'Cancelled'");

            migrationBuilder.CreateIndex(
                name: "IX_event_registrations_EventId_Status_CreatedAt",
                table: "event_registrations",
                columns: new[] { "EventId", "Status", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_event_registrations_ManageTokenHash",
                table: "event_registrations",
                column: "ManageTokenHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_events_AppId_Slug",
                table: "events",
                columns: new[] { "AppId", "Slug" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_events_AppId_Status_StartsAt",
                table: "events",
                columns: new[] { "AppId", "Status", "StartsAt" });

            migrationBuilder.CreateIndex(
                name: "IX_form_submissions_AppId",
                table: "form_submissions",
                column: "AppId");

            migrationBuilder.CreateIndex(
                name: "IX_form_submissions_AppUserId",
                table: "form_submissions",
                column: "AppUserId");

            migrationBuilder.CreateIndex(
                name: "IX_form_submissions_FormId_CreatedAt",
                table: "form_submissions",
                columns: new[] { "FormId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_forms_AppId_Slug",
                table: "forms",
                columns: new[] { "AppId", "Slug" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_library_schema_versions_AppId",
                table: "library_schema_versions",
                column: "AppId");

            migrationBuilder.CreateIndex(
                name: "IX_library_schema_versions_LibrarySchemaId_Version",
                table: "library_schema_versions",
                columns: new[] { "LibrarySchemaId", "Version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_library_schemas_app_slug",
                table: "library_schemas",
                columns: new[] { "AppId", "Slug" },
                unique: true,
                filter: "\"AppId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_library_schemas_global_slug",
                table: "library_schemas",
                column: "Slug",
                unique: true,
                filter: "\"AppId\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_member_settings_AppId",
                table: "member_settings",
                column: "AppId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_membership_events_AppId_MembershipId_CreatedAt",
                table: "membership_events",
                columns: new[] { "AppId", "MembershipId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_membership_events_MembershipId",
                table: "membership_events",
                column: "MembershipId");

            migrationBuilder.CreateIndex(
                name: "IX_membership_plans_AppId_Slug",
                table: "membership_plans",
                columns: new[] { "AppId", "Slug" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_memberships_AppId_AppUserId_open",
                table: "memberships",
                columns: new[] { "AppId", "AppUserId" },
                unique: true,
                filter: "\"Status\" IN ('Pending', 'Active', 'Suspended')");

            migrationBuilder.CreateIndex(
                name: "IX_memberships_AppId_MemberNumber",
                table: "memberships",
                columns: new[] { "AppId", "MemberNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_memberships_AppId_Status_ExpiresAt",
                table: "memberships",
                columns: new[] { "AppId", "Status", "ExpiresAt" });

            migrationBuilder.CreateIndex(
                name: "IX_memberships_AppUserId",
                table: "memberships",
                column: "AppUserId");

            migrationBuilder.CreateIndex(
                name: "IX_memberships_PlanId",
                table: "memberships",
                column: "PlanId");

            migrationBuilder.CreateIndex(
                name: "IX_payment_provider_configs_AppId_Provider",
                table: "payment_provider_configs",
                columns: new[] { "AppId", "Provider" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_payment_webhook_events_AppId_Provider_ProviderEventId",
                table: "payment_webhook_events",
                columns: new[] { "AppId", "Provider", "ProviderEventId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_search_collection_configs_app_id",
                table: "search_collection_configs",
                column: "app_id");

            migrationBuilder.CreateIndex(
                name: "IX_search_collection_configs_collection_id",
                table: "search_collection_configs",
                column: "collection_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_search_documents_app_id_language",
                table: "search_documents",
                columns: new[] { "app_id", "language" });

            migrationBuilder.CreateIndex(
                name: "IX_search_documents_app_id_section",
                table: "search_documents",
                columns: new[] { "app_id", "section" });

            migrationBuilder.CreateIndex(
                name: "IX_search_documents_app_id_source",
                table: "search_documents",
                columns: new[] { "app_id", "source" });

            migrationBuilder.CreateIndex(
                name: "IX_search_documents_app_id_source_external_id",
                table: "search_documents",
                columns: new[] { "app_id", "source", "external_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_search_documents_collection_id",
                table: "search_documents",
                column: "collection_id");

            migrationBuilder.CreateIndex(
                name: "IX_search_documents_item_id",
                table: "search_documents",
                column: "item_id");

            migrationBuilder.CreateIndex(
                name: "IX_search_documents_search_vector",
                table: "search_documents",
                column: "search_vector")
                .Annotation("Npgsql:IndexMethod", "GIN");

            migrationBuilder.CreateIndex(
                name: "IX_search_documents_title_vector",
                table: "search_documents",
                column: "title_vector")
                .Annotation("Npgsql:IndexMethod", "GIN");

            migrationBuilder.CreateIndex(
                name: "IX_search_query_logs_app_id_created_at",
                table: "search_query_logs",
                columns: new[] { "app_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "IX_search_query_logs_created_at",
                table: "search_query_logs",
                column: "created_at");

            migrationBuilder.CreateIndex(
                name: "IX_search_reindex_jobs_app_id_created_at",
                table: "search_reindex_jobs",
                columns: new[] { "app_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "IX_search_reindex_jobs_collection_id",
                table: "search_reindex_jobs",
                column: "collection_id");

            migrationBuilder.CreateIndex(
                name: "ix_search_reindex_jobs_one_pending",
                table: "search_reindex_jobs",
                columns: new[] { "app_id", "collection_id" },
                unique: true,
                filter: "status = 'pending'")
                .Annotation("Npgsql:NullsDistinct", false);

            migrationBuilder.CreateIndex(
                name: "IX_search_reindex_jobs_status_lease_expires_at",
                table: "search_reindex_jobs",
                columns: new[] { "status", "lease_expires_at" });

            migrationBuilder.CreateIndex(
                name: "IX_search_settings_app_id",
                table: "search_settings",
                column: "app_id",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_collections_library_schemas_SourceSchemaId",
                table: "collections",
                column: "SourceSchemaId",
                principalTable: "library_schemas",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            // Double-booking guard for Scheduling (not expressible in the EF model).
            migrationBuilder.Sql(Extatic.Api.Features.Scheduling.SchedulingSchema.UpSql);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(Extatic.Api.Features.Scheduling.SchedulingSchema.DownSql);

            migrationBuilder.DropForeignKey(
                name: "FK_collections_library_schemas_SourceSchemaId",
                table: "collections");

            migrationBuilder.DropTable(
                name: "app_user_auth_tokens");

            migrationBuilder.DropTable(
                name: "app_user_credentials");

            migrationBuilder.DropTable(
                name: "appointments");

            migrationBuilder.DropTable(
                name: "auth_settings");

            migrationBuilder.DropTable(
                name: "availability_overrides");

            migrationBuilder.DropTable(
                name: "availability_rules");

            migrationBuilder.DropTable(
                name: "comment_pages");

            migrationBuilder.DropTable(
                name: "comment_settings");

            migrationBuilder.DropTable(
                name: "comments");

            migrationBuilder.DropTable(
                name: "donations");

            migrationBuilder.DropTable(
                name: "event_registrations");

            migrationBuilder.DropTable(
                name: "form_submissions");

            migrationBuilder.DropTable(
                name: "library_schema_versions");

            migrationBuilder.DropTable(
                name: "member_settings");

            migrationBuilder.DropTable(
                name: "membership_events");

            migrationBuilder.DropTable(
                name: "payment_provider_configs");

            migrationBuilder.DropTable(
                name: "payment_webhook_events");

            migrationBuilder.DropTable(
                name: "search_collection_configs");

            migrationBuilder.DropTable(
                name: "search_documents");

            migrationBuilder.DropTable(
                name: "search_query_logs");

            migrationBuilder.DropTable(
                name: "search_reindex_jobs");

            migrationBuilder.DropTable(
                name: "search_settings");

            migrationBuilder.DropTable(
                name: "appointment_types");

            migrationBuilder.DropTable(
                name: "donation_campaigns");

            migrationBuilder.DropTable(
                name: "events");

            migrationBuilder.DropTable(
                name: "forms");

            migrationBuilder.DropTable(
                name: "library_schemas");

            migrationBuilder.DropTable(
                name: "memberships");

            migrationBuilder.DropTable(
                name: "booking_calendars");

            migrationBuilder.DropTable(
                name: "membership_plans");

            migrationBuilder.DropIndex(
                name: "IX_collections_SourceSchemaId",
                table: "collections");

            migrationBuilder.DropColumn(
                name: "SourceSchemaId",
                table: "collections");

            migrationBuilder.DropColumn(
                name: "SourceSchemaVersion",
                table: "collections");

            migrationBuilder.DropColumn(
                name: "DisabledAt",
                table: "app_users");

            migrationBuilder.DropColumn(
                name: "EmailVerifiedAt",
                table: "app_users");
        }
    }
}

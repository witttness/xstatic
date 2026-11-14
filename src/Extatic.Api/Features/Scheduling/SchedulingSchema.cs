namespace Extatic.Api.Features.Scheduling;

/// <summary>
/// Raw SQL that EF Core cannot express. The combined migration MUST run <see cref="UpSql"/> after
/// creating the <c>appointments</c> table (and <see cref="DownSql"/> before dropping it):
/// <code>migrationBuilder.Sql(Extatic.Api.Features.Scheduling.SchedulingSchema.UpSql);</code>
/// The exclusion constraint is what makes double booking impossible: two Pending/Confirmed
/// appointments on the same calendar can never have overlapping buffered blocks, whatever the
/// application does. Violations surface as SQLSTATE 23P01 (<see cref="ExclusionViolationSqlState"/>).
/// </summary>
public static class SchedulingSchema
{
    public const string ExclusionConstraintName = "ex_appointments_no_overlap";
    public const string ExclusionViolationSqlState = "23P01";

    public const string UpSql = """
        CREATE EXTENSION IF NOT EXISTS btree_gist;
        ALTER TABLE appointments
            ADD CONSTRAINT ex_appointments_no_overlap
            EXCLUDE USING gist (
                "CalendarId" WITH =,
                tstzrange("BlockStartsAt", "BlockEndsAt") WITH &&
            ) WHERE ("Status" IN ('Pending', 'Confirmed'));
        """;

    public const string DownSql = """
        ALTER TABLE appointments DROP CONSTRAINT IF EXISTS ex_appointments_no_overlap;
        """;
}

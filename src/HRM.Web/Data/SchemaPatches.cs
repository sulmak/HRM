using Microsoft.EntityFrameworkCore;

namespace HRM.Web.Data;

/// <summary>
/// EnsureCreated() never alters an existing database, so columns added after the first run are added here.
/// Each statement is idempotent. Replace with EF migrations before production.
/// </summary>
public static class SchemaPatches
{
    public static void Apply(HrmDbContext db)
    {
        db.Database.ExecuteSqlRaw(
            "IF COL_LENGTH('AttendanceLogs', 'ShiftId') IS NULL ALTER TABLE AttendanceLogs ADD ShiftId int NULL");
        db.Database.ExecuteSqlRaw(
            "IF COL_LENGTH('AttendanceDailies', 'ShiftId') IS NULL ALTER TABLE AttendanceDailies ADD ShiftId int NULL");
    }
}

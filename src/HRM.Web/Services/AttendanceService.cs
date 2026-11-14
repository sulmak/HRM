using System.Globalization;
using HRM.Web.Data;
using HRM.Web.Models;
using Microsoft.EntityFrameworkCore;

namespace HRM.Web.Services;

public record AttendanceEntryDto(int EmployeeId, DateOnly Date, TimeOnly? InTime, TimeOnly? OutTime, string? Remarks, int? ShiftId = null);
public record AttendanceProcessDto(DateOnly From, DateOnly To, int? EmployeeId);
public record AttendanceProcessResult(int EmployeesProcessed, int DaysProcessed, Dictionary<string, int> StatusCounts);
public record PunchDto(string EmployeeCode, DateTime Timestamp);
public record PunchImportResult(int PunchesReceived, int DaysSaved, int SkippedManual, List<string> Problems);

public class AttendanceService
{
    private readonly HrmDbContext _db;
    public AttendanceService(HrmDbContext db) => _db = db;

    /// <summary>Attendance entry: creates or updates the raw log for an employee/day.</summary>
    public async Task<AttendanceLog> SaveEntryAsync(AttendanceEntryDto dto, string source = "Manual")
    {
        if (!await _db.Employees.AnyAsync(e => e.Id == dto.EmployeeId))
            throw new KeyNotFoundException("Employee not found");
        if (dto.InTime == null && dto.OutTime != null)
            throw new InvalidOperationException("Out time needs an in time.");
        if (dto.ShiftId.HasValue && !await _db.Shifts.AnyAsync(s => s.Id == dto.ShiftId.Value))
            throw new KeyNotFoundException("Shift not found");
        await EnsureNotLockedAsync(dto.Date, dto.Date);

        var log = await _db.AttendanceLogs.FirstOrDefaultAsync(a => a.EmployeeId == dto.EmployeeId && a.Date == dto.Date);
        if (log == null)
        {
            log = new AttendanceLog { EmployeeId = dto.EmployeeId, Date = dto.Date };
            _db.AttendanceLogs.Add(log);
        }
        log.InTime = dto.InTime;
        log.OutTime = dto.OutTime;
        log.Remarks = dto.Remarks;
        log.ShiftId = dto.ShiftId;
        log.Source = source;
        await _db.SaveChangesAsync();
        return log;
    }

    /// <summary>
    /// Parses a punch export from an attendance device. One punch per line: employee code (or device user id),
    /// then date and time, separated by comma, tab, semicolon or space. Header and bad lines are skipped.
    /// </summary>
    public static List<PunchDto> ParsePunchFile(string text, List<string> problems)
    {
        var result = new List<PunchDto>();
        var lineNo = 0;
        foreach (var raw in text.Split('\n'))
        {
            lineNo++;
            var line = raw.Trim();
            if (line.Length == 0) continue;

            var parts = line.Split(new[] { ',', '\t', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (parts.Length < 2)
                parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            DateTime ts = default;
            var ok = false;
            if (parts.Length >= 3)
                ok = DateTime.TryParse(parts[1] + " " + parts[2], CultureInfo.InvariantCulture, DateTimeStyles.None, out ts);
            if (!ok && parts.Length >= 2)
                ok = DateTime.TryParse(parts[1], CultureInfo.InvariantCulture, DateTimeStyles.None, out ts);

            if (!ok)
            {
                if (lineNo > 1) problems.Add($"Line {lineNo}: could not read a date and time.");
                continue;
            }
            result.Add(new PunchDto(parts[0], ts));
        }
        return result;
    }

    private static Employee? ResolveEmployee(string code, Dictionary<string, Employee> byCode)
    {
        code = code.Trim();
        if (byCode.TryGetValue(code.ToUpperInvariant(), out var e)) return e;
        if (int.TryParse(code, out var n) && byCode.TryGetValue($"EMP-{n:00000}", out e)) return e;
        return null;
    }

    /// <summary>
    /// Turns device punches into attendance entries: the first punch of a day is In, the last is Out.
    /// Entries typed in manually are never overwritten; earlier device entries are replaced.
    /// </summary>
    public async Task<PunchImportResult> ImportPunchesAsync(List<PunchDto> punches, List<string> problems)
    {
        if (punches.Count == 0) throw new InvalidOperationException("No punches found in the file.");

        var employees = await _db.Employees.ToListAsync();
        var byCode = employees.ToDictionary(e => e.EmployeeCode.ToUpperInvariant());

        var groups = new Dictionary<(int EmployeeId, DateOnly Date), List<TimeOnly>>();
        var unknown = new Dictionary<string, int>();
        foreach (var p in punches)
        {
            var code = (p.EmployeeCode ?? "").Trim();
            var emp = ResolveEmployee(code, byCode);
            if (emp == null)
            {
                unknown[code] = unknown.GetValueOrDefault(code) + 1;
                continue;
            }
            var date = DateOnly.FromDateTime(p.Timestamp);
            if (date < emp.JoiningDate) continue;

            var key = (emp.Id, date);
            if (!groups.TryGetValue(key, out var times))
            {
                times = new List<TimeOnly>();
                groups[key] = times;
            }
            times.Add(TimeOnly.FromDateTime(p.Timestamp));
        }
        foreach (var u in unknown)
            problems.Add($"Unknown employee code '{u.Key}': {u.Value} punch(es) ignored.");

        if (groups.Count == 0)
            return new PunchImportResult(punches.Count, 0, 0, problems.Take(20).ToList());

        var minDate = groups.Keys.Min(k => k.Date);
        var maxDate = groups.Keys.Max(k => k.Date);
        await EnsureNotLockedAsync(minDate, maxDate);

        var ids = groups.Keys.Select(k => k.EmployeeId).Distinct().ToList();
        var existing = (await _db.AttendanceLogs
                .Where(l => ids.Contains(l.EmployeeId) && l.Date >= minDate && l.Date <= maxDate).ToListAsync())
            .ToDictionary(l => (l.EmployeeId, l.Date));

        var saved = 0;
        var skipped = 0;
        foreach (var (key, times) in groups)
        {
            var first = times.Min();
            var last = times.Max();
            TimeOnly? outTime = last.ToTimeSpan() - first.ToTimeSpan() >= TimeSpan.FromMinutes(1) ? last : null;

            existing.TryGetValue(key, out var log);
            if (log != null)
            {
                if (log.Source != "Device") { skipped++; continue; }
            }
            else
            {
                log = new AttendanceLog { EmployeeId = key.EmployeeId, Date = key.Date };
                _db.AttendanceLogs.Add(log);
            }
            log.InTime = first;
            log.OutTime = outTime;
            log.Source = "Device";
            saved++;
        }
        await _db.SaveChangesAsync();

        if (skipped > 0)
            problems.Add($"{skipped} day(s) already had a manual entry and were left unchanged.");
        return new PunchImportResult(punches.Count, saved, skipped, problems.Take(20).ToList());
    }

    /// <summary>Clears an entry (employee absent / no punch). Returns true when something was removed.</summary>
    public async Task<bool> RemoveEntryAsync(int employeeId, DateOnly date)
    {
        var log = await _db.AttendanceLogs.FirstOrDefaultAsync(a => a.EmployeeId == employeeId && a.Date == date);
        if (log == null) return false;
        await EnsureNotLockedAsync(date, date);
        _db.AttendanceLogs.Remove(log);
        await _db.SaveChangesAsync();
        return true;
    }

    /// <summary>Months that already have a finalized / paid payroll cannot be re-processed.</summary>
    private async Task EnsureNotLockedAsync(DateOnly from, DateOnly to)
    {
        var months = DateRules.Range(from, to).Select(d => (d.Year, d.Month)).Distinct().ToList();
        var locked = await _db.PayrollRuns.Where(r => r.Status != PayrollStatus.Draft).ToListAsync();
        var hit = locked.FirstOrDefault(r => months.Contains((r.Year, r.Month)));
        if (hit != null)
            throw new InvalidOperationException($"Payroll for {hit.Year}-{hit.Month:00} is already {hit.Status}; attendance is locked.");
    }

    /// <summary>
    /// Attendance process: turns raw logs, leave, holidays and weekly offs into one status per employee per day.
    /// </summary>
    public async Task<AttendanceProcessResult> ProcessAsync(AttendanceProcessDto dto)
    {
        if (dto.To < dto.From) throw new InvalidOperationException("To date is before from date.");
        if (dto.To.DayNumber - dto.From.DayNumber > 92) throw new InvalidOperationException("Process at most 93 days at a time.");
        await EnsureNotLockedAsync(dto.From, dto.To);

        var empQuery = _db.Employees.Include(e => e.Shift).AsQueryable();
        if (dto.EmployeeId.HasValue) empQuery = empQuery.Where(e => e.Id == dto.EmployeeId.Value);
        var employees = await empQuery.ToListAsync();
        var ids = employees.Select(e => e.Id).ToList();

        var logs = (await _db.AttendanceLogs
                .Where(l => ids.Contains(l.EmployeeId) && l.Date >= dto.From && l.Date <= dto.To).ToListAsync())
            .ToDictionary(l => (l.EmployeeId, l.Date));
        var leaves = await _db.LeaveApplications
            .Where(l => ids.Contains(l.EmployeeId) && l.Status == LeaveStatus.Approved &&
                        l.FromDate <= dto.To && l.ToDate >= dto.From).ToListAsync();
        var holidays = (await _db.Holidays
                .Where(h => h.Date >= dto.From && h.Date <= dto.To && !h.IsOptional).Select(h => h.Date).ToListAsync())
            .ToHashSet();
        var existing = (await _db.AttendanceDailies
                .Where(a => ids.Contains(a.EmployeeId) && a.Date >= dto.From && a.Date <= dto.To).ToListAsync())
            .ToDictionary(a => (a.EmployeeId, a.Date));
        var defaultShift = await _db.Shifts.OrderBy(s => s.Id).FirstOrDefaultAsync()
                           ?? new Shift { StartTime = new TimeOnly(9, 0), EndTime = new TimeOnly(18, 0), GraceMinutes = 10, HalfDayHours = 4 };

        var shiftMap = await _db.Shifts.ToDictionaryAsync(s => s.Id);

        var counts = Enum.GetNames<AttendanceStatus>().ToDictionary(n => n, _ => 0);
        var days = 0;
        var empsTouched = new HashSet<int>();

        foreach (var emp in employees)
        {
            var empShift = emp.Shift ?? defaultShift;
            var off = DateRules.ParseOffDays(emp.WeeklyOffDays);
            var empLeaves = leaves.Where(l => l.EmployeeId == emp.Id).ToList();

            foreach (var date in DateRules.Range(dto.From, dto.To))
            {
                if (date < emp.JoiningDate) continue;
                if (emp.LastWorkingDate.HasValue && date > emp.LastWorkingDate.Value) continue;

                logs.TryGetValue((emp.Id, date), out var log);

                // Shift worked that day: the one chosen on the entry sheet, else the employee's default shift.
                var shift = empShift;
                if (log?.ShiftId != null && shiftMap.TryGetValue(log.ShiftId.Value, out var dayShift))
                    shift = dayShift;
                var standardHours = (decimal)(shift.EndTime - shift.StartTime).TotalHours;

                var isOff = off.Contains(date.DayOfWeek);
                var isHoliday = holidays.Contains(date);

                AttendanceStatus status;
                int late = 0;
                decimal worked = 0, overtime = 0;

                if (log?.InTime != null)
                {
                    if (log.OutTime != null)
                    {
                        var span = log.OutTime.Value.ToTimeSpan() - log.InTime.Value.ToTimeSpan();
                        if (span < TimeSpan.Zero) span += TimeSpan.FromHours(24); // night shift
                        worked = Math.Round((decimal)span.TotalHours, 2);
                    }

                    if (isOff || isHoliday)
                    {
                        status = AttendanceStatus.Present; // worked on a day off: all hours are overtime
                        overtime = worked;
                    }
                    else
                    {
                        var lateAfter = shift.StartTime.AddMinutes(shift.GraceMinutes);
                        if (log.InTime.Value > lateAfter)
                            late = (int)(log.InTime.Value - shift.StartTime).TotalMinutes;

                        if (log.OutTime != null && worked < shift.HalfDayHours) status = AttendanceStatus.HalfDay;
                        else status = late > 0 ? AttendanceStatus.Late : AttendanceStatus.Present;

                        if (worked > standardHours) overtime = Math.Round(worked - standardHours, 2);
                    }
                }
                else if (isHoliday) status = AttendanceStatus.Holiday;
                else if (isOff) status = AttendanceStatus.WeeklyOff;
                else if (empLeaves.Any(l => l.FromDate <= date && l.ToDate >= date)) status = AttendanceStatus.OnLeave;
                else status = AttendanceStatus.Absent;

                if (!existing.TryGetValue((emp.Id, date), out var daily))
                {
                    daily = new AttendanceDaily { EmployeeId = emp.Id, Date = date };
                    _db.AttendanceDailies.Add(daily);
                }
                daily.Status = status;
                daily.InTime = log?.InTime;
                daily.OutTime = log?.OutTime;
                daily.LateMinutes = late;
                daily.WorkedHours = worked;
                daily.OvertimeHours = overtime;
                daily.ShiftId = shift.Id == 0 ? null : shift.Id;
                daily.ProcessedOn = DateTime.UtcNow;

                counts[status.ToString()]++;
                days++;
                empsTouched.Add(emp.Id);
            }
        }

        await _db.SaveChangesAsync();
        return new AttendanceProcessResult(empsTouched.Count, days, counts);
    }
}

# HRM system

![HRM cover](docs/cover.png)

ASP.NET Core 8 (MVC host + Web API, EF Core, SQL Server) with an Angular 18 (TypeScript) client.

```
HRM/
  HRM.sln
  src/HRM.Web/     .NET backend (Models, Data, Services, Controllers)
  client/          Angular app (builds into src/HRM.Web/wwwroot)
```

## Run

Prerequisites: .NET 8 SDK, Node 20+, SQL Server (LocalDB on Windows by default).

```bash
# 1. backend  (creates and seeds the HrmDb database on first start)
cd src/HRM.Web
dotnet run                      # http://localhost:5080, API docs at /swagger

# 2a. frontend, development (hot reload, proxies /api to :5080)
cd client
npm install
npm start                       # http://localhost:4200

# 2b. frontend, served by the .NET app
npm run build                   # output goes to src/HRM.Web/wwwroot, then open http://localhost:5080
```

Not on Windows / no LocalDB? Change `ConnectionStrings:Default` in `appsettings.json` to any SQL Server instance.

## Modules

| # | Requirement | Where |
|---|-------------|-------|
| Selection i-viii | circular, CV drop, interview, merit list + approval, final selection/rejection, appointment letter, joining letter, add as employee | `RecruitmentService`, `/recruitment/*` screens |
| 1 | Employee information | `EmployeesController`, Employees screen |
| 2-5 | Leave, earned, casual, other leave | `LeaveService`, Leave screen |
| 6 | Weekly off day calculation | `WeeklyOffController`, Weekly off screen |
| 7 | Employee calendar | `CalendarController`, Calendar screen |
| 8 | Attendance entry | `AttendanceController` `/entry`, Attendance > Entry |
| 9 | Attendance process | `AttendanceService.ProcessAsync`, Attendance > Process |
| 10 | Salary process / payroll | `PayrollService`, Payroll screen |
| 11 | Performance measure | `PerformanceController`, Performance screen |

### Recruitment flow
`Applied -> Shortlisted -> Interviewed -> MeritListed -> Approved -> Selected -> Appointed -> JoiningSubmitted -> Joined`
(`Rejected` is possible at approval and final selection). Every step checks the candidate's current stage, so steps cannot be skipped. Selection stops once the circular's vacancies are filled. "Add as employee" creates the employee and a salary structure split from the offered gross (60% basic, 20% house rent, 10% medical, 10% conveyance; edit afterwards).

### Leave rules (editable on Leave > Policies)
- Casual 10 days and sick 14 days a year, prorated by remaining months in the joining year.
- Earned: 1 day per 18 days worked in each completed year (plus any opening balance), capped at 60, usable after 12 months of service.
- Maternity 112 days (female), paternity 7 days (male), unpaid leave (no limit, deducted in payroll).
- Weekly offs and holidays inside a leave range are not counted (maternity/paternity run on calendar days). Overlaps and insufficient balances are rejected.

### Attendance and payroll
1. Enter in/out times per day (bulk sheet).
2. Process: status per employee per day = Present, Late (after shift start + grace), HalfDay (worked less than the shift's half-day hours), Absent, OnLeave, WeeklyOff, Holiday. Work on an off day or holiday counts as overtime.
3. Payroll for a month: gross prorated for joining/leaving mid month, minus absence and unpaid leave (gross / days in month per day), minus one day per N late arrivals (`Payroll:LateDaysPerAbsent`), provident fund (% of basic) and tax, plus overtime, calculated shift by shift: each day's overtime hours are valued at `basic / Payroll:WorkingDaysPerMonth (26) / that shift's hours x Payroll:OvertimeMultiplier (2)`. The shift of each day is chosen on the attendance entry sheet (default: the employee's shift) and stored when attendance is processed; the payslip shows a shift-wise breakdown. Days processed before shifts were tracked fall back to the employee's current shift (payroll warns; re-run attendance processing to fix). New columns `ShiftId` on `AttendanceLogs`/`AttendanceDailies` are added automatically at startup by `Data/SchemaPatches.cs`. Finalizing a payroll locks that month's attendance.

### Attendance device
- **Import:** Attendance > Device tab. Upload the punch log exported from the device (CSV/TXT, one punch per line: employee code or device user ID, then `yyyy-MM-dd HH:mm:ss`). First punch of a day = In, last = Out. Entries typed manually are never overwritten.
- **Live push:** `POST /api/attendance/device/punches` with `{"punches":[{"employeeCode":"EMP-00003","timestamp":"2026-10-12T09:02:11"}]}`. Set `Device:ApiKey` in `appsettings.json` to require an `X-Api-Key` header.
- A device user ID of `3` maps to employee `EMP-00003`; otherwise the device user ID must equal the employee code. Talking to a device directly over its own protocol (for example ZKTeco SDK) is not included; use the device software's export or push.
- After importing, run Attendance > Process for those dates.

### Performance
KPI scores (1-5, weighted) give a 0-100 KPI score; attendance discipline is derived from processed attendance in the cycle; overall = 80% KPI + 20% attendance (`Performance` section of `appsettings.json`). Ratings: Outstanding (90+), Exceeds (75+), Meets (60+), Needs improvement (40+), Unsatisfactory.

## Before production
- There is **no authentication or role-based authorization** yet: anyone who can reach the API can approve leave or run payroll. Add ASP.NET Core Identity/JWT and roles (HR, manager, employee) first.
- The schema is created with `EnsureCreated()` in `Program.cs`. Switch to migrations: `dotnet ef migrations add Initial` then `dotnet ef database update`, and remove `EnsureCreated`.
- Uploaded CVs are stored in `App_Data/cv` (not publicly served; downloaded through the API).
- Seed data (departments, a shift, sample employees, fixed-date Bangladesh holidays) is in `Data/DbSeeder.cs`. Movable holidays (Eid etc.) must be added each year via `POST /api/lookups/holidays`.
- Leave and payroll rules are sensible defaults, not legal advice; check them against your company policy and local labour law.

---

**Powered by Md. Atikur Rahman Mridha**

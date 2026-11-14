import { Component, OnInit, inject } from '@angular/core';
import { DatePipe, DecimalPipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Api, Notify } from '../core/api.service';
import { MONTH_NAMES, badge, hm, monthEnd, monthStart, nz, toTime, today } from '../core/util';

@Component({
  selector: 'app-attendance',
  standalone: true,
  imports: [FormsModule, DatePipe, DecimalPipe],
  template: `
    <h1>Attendance</h1>
    <p class="sub">Enter in/out times, process them into daily statuses, then review.</p>

    <div class="tabs">
      @for (t of tabs; track t.id) { <button [class.on]="tab === t.id" (click)="tab = t.id; onTab()">{{ t.label }}</button> }
    </div>

    @if (tab === 'entry') {
      <div class="card">
        <div class="row">
          <div><label>Date</label><input type="date" class="inline" [(ngModel)]="date" (ngModelChange)="loadEntries()" /></div>
          <div><label>Shift</label>
            <select class="inline" [(ngModel)]="applyShiftId">
              <option [ngValue]="0">Each employee's own shift</option>
              @for (s of shifts; track s.id) { <option [ngValue]="s.id">{{ s.name }}</option> }
            </select></div>
          <label class="chk" style="margin-bottom: 8px"><input type="checkbox" [(ngModel)]="overwrite" /> Replace filled rows</label>
          <button class="ghost" (click)="applyShift()">Apply</button>
          <button (click)="saveEntries()">Save day</button>
        </div>
        <p class="muted small">Pick each employee's shift for the day in the Shift column (needed for rotating shifts); payroll uses it for late and overtime. Apply fills the shift and In/Out times of rows from the chosen shift (blank rows only, unless "Replace filled rows" is ticked); nothing is saved until you click Save day. Leave "In" empty for employees with no punch. Saving a row with an empty "In" clears an existing entry. Punches from the attendance device come in through the Device tab.</p>
      </div>
      <div class="card scroll">
        <table>
          <thead><tr><th>Code</th><th>Employee</th><th>Shift</th><th>In</th><th>Out</th><th>Remarks</th></tr></thead>
          <tbody>
            @for (r of rows; track r.employeeId) {
              <tr>
                <td>{{ r.employeeCode }}</td><td>{{ r.fullName }}</td><td>
                  <select [(ngModel)]="r.shiftId" style="min-width: 150px">
                    <option [ngValue]="null">-</option>
                    @for (s of shifts; track s.id) { <option [ngValue]="s.id">{{ s.name }}</option> }
                  </select>
                </td>
                <td><input type="time" [(ngModel)]="r.inTime" style="width: 120px" /></td>
                <td><input type="time" [(ngModel)]="r.outTime" style="width: 120px" /></td>
                <td><input [(ngModel)]="r.remarks" /></td>
              </tr>
            } @empty { <tr><td colspan="6" class="empty">No employees were employed on this date.</td></tr> }
          </tbody>
        </table>
      </div>
    }

    @if (tab === 'process') {
      <div class="card">
        <h2>Attendance process</h2>
        <p class="muted small">Combines punches, approved leave, holidays and weekly offs into one status per employee per day. Safe to re-run; months with a finalized payroll are locked.</p>
        <div class="row">
          <div><label>From</label><input type="date" class="inline" [(ngModel)]="pFrom" /></div>
          <div><label>To</label><input type="date" class="inline" [(ngModel)]="pTo" /></div>
          <div><label>Employee</label>
            <select class="inline" [(ngModel)]="pEmployee">
              <option [ngValue]="null">All employees</option>
              @for (e of employees; track e.id) { <option [ngValue]="e.id">{{ e.employeeCode }} - {{ e.fullName }}</option> }
            </select></div>
          <button (click)="process()">Process</button>
        </div>
        @if (result) {
          <p style="margin-top: 12px"><b>{{ result.daysProcessed }}</b> day(s) processed for <b>{{ result.employeesProcessed }}</b> employee(s).</p>
          <div class="actions">
            @for (k of statusKeys(result.statusCounts); track k) {
              <span class="badge {{ badge(k) }}">{{ k }}: {{ result.statusCounts[k] }}</span>
            }
          </div>
        }
      </div>
    }

    @if (tab === 'device') {
      <div class="card">
        <h2>Import from attendance device</h2>
        <p class="muted small">Export the punch log from the device (USB or its software) as a CSV or TXT file, one punch per line: employee code or device user ID, then date and time. Examples: <code>EMP-00003,2026-10-12 09:02:11</code> or <code>3 2026-10-12 09:02:11</code>. The first punch of the day becomes In and the last becomes Out. Use yyyy-MM-dd dates. Manual entries are never overwritten.</p>
        <div class="row">
          <div class="grow"><label>Punch file</label><input #devFile type="file" accept=".csv,.txt,.dat,.log" /></div>
          <button [disabled]="busy" (click)="importFile(devFile)">Import punches</button>
        </div>
        @if (imp) {
          <div class="panel">
            <b>{{ imp.daysSaved }}</b> day(s) saved from <b>{{ imp.punchesReceived }}</b> punch(es).
            @for (p of imp.problems; track p) { <div class="small">{{ p }}</div> }
            <div class="small" style="margin-top: 6px">Next: open the Process tab and process these dates to get statuses.</div>
          </div>
        }
      </div>

      <div class="card">
        <h2>Live push from the device or its software</h2>
        <p class="muted small">If the device software can call a web address, send punches to the endpoint below. Times are local time without a time zone. To require a key, set <code>Device:ApiKey</code> in appsettings.json and send it in the <code>X-Api-Key</code> header.</p>
        <p><b>POST</b> <code>{{ endpoint }}</code></p>
        <pre style="background: #f4f6f9; padding: 10px; border-radius: 6px; overflow-x: auto">{{ sample }}</pre>
      </div>
    }

    @if (tab === 'review') {
      <div class="card">
        <div class="row">
          <div><label>View</label>
            <select class="inline" [(ngModel)]="view" (ngModelChange)="loadReview()"><option value="summary">Monthly summary</option><option value="daily">Daily records</option></select></div>
          @if (view === 'summary') {
            <div><label>Month</label>
              <select class="inline" [(ngModel)]="month" (ngModelChange)="loadReview()">
                @for (m of monthNames; track m; let i = $index) { <option [ngValue]="i + 1">{{ m }}</option> }
              </select></div>
            <div><label>Year</label><input type="number" class="inline" style="width: 90px" [(ngModel)]="year" (ngModelChange)="loadReview()" /></div>
          } @else {
            <div><label>From</label><input type="date" class="inline" [(ngModel)]="rFrom" (ngModelChange)="loadReview()" /></div>
            <div><label>To</label><input type="date" class="inline" [(ngModel)]="rTo" (ngModelChange)="loadReview()" /></div>
            <div><label>Employee</label>
              <select class="inline" [(ngModel)]="rEmployee" (ngModelChange)="loadReview()">
                <option [ngValue]="null">All</option>
                @for (e of employees; track e.id) { <option [ngValue]="e.id">{{ e.employeeCode }} - {{ e.fullName }}</option> }
              </select></div>
          }
        </div>
      </div>

      <div class="card scroll">
        @if (view === 'summary') {
          <table>
            <thead><tr><th>Code</th><th>Employee</th><th class="num">Present</th><th class="num">Late</th><th class="num">Half day</th><th class="num">Absent</th><th class="num">On leave</th><th class="num">Weekly off</th><th class="num">Holiday</th><th class="num">OT hrs</th></tr></thead>
            <tbody>
              @for (s of summary; track s.employeeId) {
                <tr><td>{{ s.employeeCode }}</td><td>{{ s.fullName }}</td>
                  <td class="num">{{ s.present }}</td><td class="num">{{ s.late }}</td><td class="num">{{ s.halfDay }}</td>
                  <td class="num">{{ s.absent }}</td><td class="num">{{ s.onLeave }}</td><td class="num">{{ s.weeklyOff }}</td>
                  <td class="num">{{ s.holiday }}</td><td class="num">{{ s.overtimeHours | number: '1.0-2' }}</td></tr>
              } @empty { <tr><td colspan="10" class="empty">Nothing processed for this month.</td></tr> }
            </tbody>
          </table>
        } @else {
          <table>
            <thead><tr><th>Date</th><th>Employee</th><th>Shift</th><th>Status</th><th>In</th><th>Out</th><th class="num">Late (min)</th><th class="num">Worked</th><th class="num">OT</th></tr></thead>
            <tbody>
              @for (d of daily; track d.id) {
                <tr><td>{{ d.date | date: 'EEE d MMM' }}</td><td>{{ d.employeeCode }} {{ d.employeeName }}</td>
                  <td class="muted small">{{ shiftName(d.shiftId) }}</td>
                  <td><span class="badge {{ badge(d.status) }}">{{ d.status }}</span></td>
                  <td>{{ hm(d.inTime) }}</td><td>{{ hm(d.outTime) }}</td>
                  <td class="num">{{ d.lateMinutes || '' }}</td>
                  <td class="num">{{ d.workedHours ? (d.workedHours | number: '1.0-2') : '' }}</td>
                  <td class="num">{{ d.overtimeHours ? (d.overtimeHours | number: '1.0-2') : '' }}</td></tr>
              } @empty { <tr><td colspan="9" class="empty">No processed records in this range.</td></tr> }
            </tbody>
          </table>
        }
      </div>
    }
  `
})
export class AttendanceComponent implements OnInit {
  private api = inject(Api);
  private n = inject(Notify);
  badge = badge;
  hm = hm;
  monthNames = MONTH_NAMES;
  tabs = [{ id: 'entry', label: 'Entry' }, { id: 'device', label: 'Device' }, { id: 'process', label: 'Process' }, { id: 'review', label: 'Review' }];
  tab = 'entry';
  employees: any[] = [];
  shifts: any[] = [];
  applyShiftId = 0;
  overwrite = false;
  busy = false;
  imp: any = null;
  endpoint = `http://${location.hostname}:5080/api/attendance/device/punches`;
  sample = JSON.stringify({ punches: [{ employeeCode: 'EMP-00003', timestamp: '2026-10-12T09:02:11' }] }, null, 2);

  date = today();
  rows: any[] = [];

  pFrom = monthStart(new Date().getFullYear(), new Date().getMonth() + 1);
  pTo = today();
  pEmployee: number | null = null;
  result: any = null;

  view = 'summary';
  year = new Date().getFullYear();
  month = new Date().getMonth() + 1;
  rFrom = monthStart(this.year, this.month);
  rTo = monthEnd(this.year, this.month);
  rEmployee: number | null = null;
  summary: any[] = [];
  daily: any[] = [];

  async ngOnInit() {
    this.employees = (await this.api.get<any[]>('employees')) ?? [];
    this.shifts = (await this.api.get<any[]>('shifts')) ?? [];
    await this.loadEntries();
  }

  async onTab() {
    if (this.tab === 'entry') await this.loadEntries();
    if (this.tab === 'review') await this.loadReview();
  }

  async loadEntries() {
    const list = (await this.api.get<any[]>('attendance/entries', { date: this.date })) ?? [];
    this.rows = list.map(r => ({
      ...r, inTime: hm(r.inTime), outTime: hm(r.outTime), remarks: r.remarks ?? '',
      // the shift saved for that day, else the employee's default shift
      shiftId: r.shiftId ?? this.employees.find(e => e.id === r.employeeId)?.shiftId ?? null
    }));
  }

  shiftName(id: number | null | undefined) { return this.shifts.find(s => s.id === id)?.name ?? '-'; }

  /** Fills In/Out of the rows from the chosen shift (or each employee's own shift). Not saved until "Save day". */
  applyShift() {
    let filled = 0;
    let noShift = 0;
    for (const r of this.rows) {
      if (r.inTime && !this.overwrite) continue;
      const s = this.shifts.find(x => x.id === (this.applyShiftId || r.shiftId));
      if (!s) { noShift++; continue; }
      r.inTime = hm(s.startTime);
      r.outTime = hm(s.endTime);
      r.shiftId = s.id;
      filled++;
    }
    if (filled === 0 && noShift === 0) this.n.error('Every row already has times. Tick "Replace filled rows" to overwrite them.');
    else this.n.ok(`Shift times applied to ${filled} row(s)${noShift ? `; ${noShift} employee(s) have no shift` : ''}. Click Save day to keep them.`);
  }

  async importFile(input: HTMLInputElement) {
    const file = input.files?.[0];
    if (!file) { this.n.error('Choose a punch file first.'); return; }
    const fd = new FormData();
    fd.append('file', file);
    this.busy = true;
    this.imp = await this.api.postForm('attendance/device/import', fd);
    this.busy = false;
    if (this.imp) { this.n.ok('Punches imported.'); input.value = ''; }
  }

  async saveEntries() {
    for (const r of this.rows) {
      if (r.outTime && !r.inTime) { this.n.error(`${r.fullName}: out time needs an in time.`); return; }
    }
    const body = {
      date: this.date,
      rows: this.rows.map(r => ({ employeeId: r.employeeId, inTime: toTime(r.inTime), outTime: toTime(r.outTime), remarks: nz(r.remarks), shiftId: r.shiftId ?? null }))
    };
    const res: any = await this.api.post('attendance/entry/bulk', body);
    if (res) { this.n.ok(`${res.saved} entr${res.saved === 1 ? 'y' : 'ies'} saved. Run the process step to update statuses.`); await this.loadEntries(); }
  }

  async process() {
    this.result = null;
    this.result = await this.api.post('attendance/process', { from: this.pFrom, to: this.pTo, employeeId: this.pEmployee });
    if (this.result) this.n.ok('Attendance processed.');
  }

  statusKeys(c: Record<string, number>) { return Object.keys(c).filter(k => c[k] > 0); }

  async loadReview() {
    if (this.view === 'summary') {
      this.summary = (await this.api.get<any[]>('attendance/summary', { year: this.year, month: this.month })) ?? [];
    } else {
      this.daily = (await this.api.get<any[]>('attendance/daily', { from: this.rFrom, to: this.rTo, employeeId: this.rEmployee })) ?? [];
    }
  }
}

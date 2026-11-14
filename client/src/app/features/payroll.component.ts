import { Component, OnInit, inject } from '@angular/core';
import { DecimalPipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Api, Notify } from '../core/api.service';
import { MONTH_NAMES, badge } from '../core/util';

@Component({
  selector: 'app-payroll',
  standalone: true,
  imports: [FormsModule, DecimalPipe],
  template: `
    <h1>Payroll</h1>
    <p class="sub">Process the month's salary from salary structures and processed attendance, then finalize and mark as paid.</p>

    <div class="card no-print">
      <div class="row">
        <div><label>Month</label>
          <select class="inline" [(ngModel)]="month">@for (m of monthNames; track m; let i = $index) { <option [ngValue]="i + 1">{{ m }}</option> }</select></div>
        <div><label>Year</label><input type="number" class="inline" style="width: 90px" [(ngModel)]="year" /></div>
        <button (click)="process()">Process payroll</button>
      </div>
      <p class="muted small">Run <b>Attendance &rarr; Process</b> for the whole month first. Re-processing replaces the draft; finalized payrolls are locked.</p>
      @if (warnings.length) {
        <div class="panel"><b>Skipped:</b>
          @for (w of warnings; track w) { <div class="small">{{ w }}</div> }
        </div>
      }
    </div>

    <div class="card scroll no-print">
      <table>
        <thead><tr><th>Period</th><th>Status</th><th class="num">Employees</th><th class="num">Total gross</th><th class="num">Total net</th><th></th></tr></thead>
        <tbody>
          @for (r of runs; track r.id) {
            <tr>
              <td><b>{{ monthNames[r.month - 1] }} {{ r.year }}</b></td>
              <td><span class="badge {{ badge(r.status) }}">{{ r.status }}</span></td>
              <td class="num">{{ r.employees }}</td>
              <td class="num">{{ r.totalGross | number: '1.2-2' }}</td>
              <td class="num">{{ r.totalNet | number: '1.2-2' }}</td>
              <td><div class="actions">
                <button class="sm ghost" (click)="openRun(r)">View</button>
                @if (r.status === 'Draft') { <button class="sm" (click)="finalize(r)">Finalize</button> }
                @if (r.status === 'Finalized') { <button class="sm" (click)="pay(r)">Mark paid</button> }
              </div></td>
            </tr>
          } @empty { <tr><td colspan="6" class="empty">No payroll runs yet.</td></tr> }
        </tbody>
      </table>
    </div>

    @if (run) {
      <div class="card scroll no-print">
        <h2>Payslips &mdash; {{ monthNames[run.month - 1] }} {{ run.year }}</h2>
        <table>
          <thead><tr><th>Employee</th><th class="num">Payable days</th><th class="num">Absent</th><th class="num">Unpaid</th><th class="num">Late</th><th class="num">Gross</th><th class="num">OT pay</th><th class="num">Deductions</th><th class="num">Net pay</th><th></th></tr></thead>
          <tbody>
            @for (p of payslips; track p.id) {
              <tr>
                <td>{{ p.employeeCode }} {{ p.employeeName }}</td>
                <td class="num">{{ p.payableDays }}/{{ p.daysInMonth }}</td>
                <td class="num">{{ p.absentDays }}</td><td class="num">{{ p.unpaidLeaveDays }}</td><td class="num">{{ p.lateDays }}</td>
                <td class="num">{{ p.gross | number: '1.2-2' }}</td>
                <td class="num">{{ p.overtimePay | number: '1.2-2' }}</td>
                <td class="num">{{ p.totalDeductions | number: '1.2-2' }}</td>
                <td class="num"><b>{{ p.netPay | number: '1.2-2' }}</b></td>
                <td><button class="sm ghost" (click)="openSlip(p)">Payslip</button></td>
              </tr>
            } @empty { <tr><td colspan="10" class="empty">No payslips in this run.</td></tr> }
          </tbody>
        </table>
      </div>
    }

    @if (slip) {
      <div class="card payslip">
        <div class="row spread">
          <h2>Payslip &mdash; {{ slip.period }}</h2>
          <div class="actions no-print"><button class="sm ghost" (click)="print()">Print</button><button class="sm ghost" (click)="slip = null">Close</button></div>
        </div>
        <p><b>{{ slip.employeeName }}</b> ({{ slip.employeeCode }})<br />
          <span class="muted">{{ slip.designation }} {{ slip.department ? '· ' + slip.department : '' }}</span><br />
          <span class="muted small">Payable days {{ slip.payableDays }} of {{ slip.daysInMonth }} · Absent {{ slip.absentDays }} · Unpaid leave {{ slip.unpaidLeaveDays }} · Late {{ slip.lateDays }} · Overtime {{ slip.overtimeHours }} h</span></p>
        <table>
          <tr><th>Earnings</th><th></th></tr>
          <tr><td>Basic</td><td>{{ slip.basic | number: '1.2-2' }}</td></tr>
          <tr><td>House rent</td><td>{{ slip.houseRent | number: '1.2-2' }}</td></tr>
          <tr><td>Medical</td><td>{{ slip.medical | number: '1.2-2' }}</td></tr>
          <tr><td>Conveyance</td><td>{{ slip.conveyance | number: '1.2-2' }}</td></tr>
          <tr><td>Other allowance</td><td>{{ slip.otherAllowance | number: '1.2-2' }}</td></tr>
          <tr><td>Overtime</td><td>{{ slip.overtimePay | number: '1.2-2' }}</td></tr>
          <tr class="tot"><td>Total earnings</td><td>{{ slip.totalEarnings | number: '1.2-2' }}</td></tr>
          <tr><th>Deductions</th><th></th></tr>
          <tr><td>Absence / unpaid leave</td><td>{{ slip.absentDeduction | number: '1.2-2' }}</td></tr>
          <tr><td>Late arrival</td><td>{{ slip.lateDeduction | number: '1.2-2' }}</td></tr>
          <tr><td>Provident fund</td><td>{{ slip.providentFund | number: '1.2-2' }}</td></tr>
          <tr><td>Tax</td><td>{{ slip.tax | number: '1.2-2' }}</td></tr>
          <tr class="tot"><td>Total deductions</td><td>{{ slip.totalDeductions | number: '1.2-2' }}</td></tr>
          <tr class="tot"><td>Net pay</td><td>{{ slip.netPay | number: '1.2-2' }}</td></tr>
        </table>

        @if (slip.shiftBreakdown?.length) {
          <h2 style="margin-top: 18px">Shift-wise attendance</h2>
          <table>
            <tr><th>Shift</th><th>Worked days</th><th>Late</th><th>Absent</th><th>OT hours</th><th>OT pay</th></tr>
            @for (l of slip.shiftBreakdown; track l.shiftName) {
              <tr>
                <td>{{ l.shiftName }} <span class="muted small">({{ l.shiftHours | number: '1.0-2' }} h)</span></td>
                <td>{{ l.workedDays }}</td><td>{{ l.lateDays }}</td><td>{{ l.absentDays }}</td>
                <td>{{ l.overtimeHours | number: '1.0-2' }}</td>
                <td>{{ l.overtimePay | number: '1.2-2' }}</td>
              </tr>
            }
          </table>
          <p class="muted small">Overtime per day = basic &divide; working days per month &divide; that shift's hours &times; overtime multiplier.</p>
        }
      </div>
    }
  `
})
export class PayrollComponent implements OnInit {
  private api = inject(Api);
  private n = inject(Notify);
  badge = badge;
  monthNames = MONTH_NAMES;

  year = new Date().getFullYear();
  month = new Date().getMonth() + 1;
  runs: any[] = [];
  run: any = null;
  payslips: any[] = [];
  slip: any = null;
  warnings: string[] = [];

  async ngOnInit() { await this.loadRuns(); }

  async loadRuns() { this.runs = (await this.api.get<any[]>('payroll/runs')) ?? []; }

  async process() {
    const res: any = await this.api.post('payroll/process', { year: this.year, month: this.month });
    if (!res) return;
    this.warnings = res.warnings;
    this.n.ok(`Payroll processed: ${res.payslipCount} payslip(s).`);
    await this.loadRuns();
    const r = this.runs.find(x => x.id === res.runId);
    if (r) await this.openRun(r);
  }

  async openRun(r: any) {
    this.run = r;
    this.slip = null;
    this.payslips = (await this.api.get<any[]>(`payroll/runs/${r.id}/payslips`)) ?? [];
  }

  async openSlip(p: any) { this.slip = await this.api.get(`payroll/payslips/${p.id}`); }

  async finalize(r: any) {
    if (!confirm('Finalize this payroll? Attendance for the month will be locked.')) return;
    if (await this.api.post(`payroll/runs/${r.id}/finalize`)) { this.n.ok('Payroll finalized.'); await this.loadRuns(); }
  }

  async pay(r: any) {
    if (!confirm('Mark this payroll as paid?')) return;
    if (await this.api.post(`payroll/runs/${r.id}/pay`)) { this.n.ok('Payroll marked as paid.'); await this.loadRuns(); }
  }

  print() { window.print(); }
}

import { Component, OnInit, inject } from '@angular/core';
import { DatePipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Api, Notify } from '../core/api.service';
import { MONTH_NAMES, WEEK_DAYS, monthEnd, monthStart } from '../core/util';

@Component({
  selector: 'app-weekly-off',
  standalone: true,
  imports: [FormsModule, DatePipe],
  template: `
    <h1>Weekly off days</h1>
    <p class="sub">Set each employee's weekly off day(s) and calculate how many fall in a period.</p>

    <div class="card">
      <div class="row">
        <div class="grow"><label>Employee</label>
          <select [(ngModel)]="employeeId" (ngModelChange)="onEmployee()">
            @for (e of employees; track e.id) { <option [ngValue]="e.id">{{ e.employeeCode }} - {{ e.fullName }}</option> }
          </select></div>
      </div>
      <div style="margin-top: 12px">
        <label>Weekly off days</label>
        @for (d of days; track d) { <label class="chk"><input type="checkbox" [(ngModel)]="off[d]" /> {{ d }}</label> }
        <button class="sm" (click)="save()">Save</button>
      </div>
    </div>

    <div class="card">
      <h2>Calculate for a period</h2>
      <div class="row">
        <div><label>From</label><input type="date" class="inline" [(ngModel)]="from" /></div>
        <div><label>To</label><input type="date" class="inline" [(ngModel)]="to" /></div>
        <button (click)="calculate()">Calculate</button>
      </div>
      @if (result) {
        <p style="margin-top: 12px"><b>{{ result.count }}</b> weekly off day(s) between {{ from | date: 'd MMM yyyy' }} and {{ to | date: 'd MMM yyyy' }}.</p>
        <div class="muted small">
          @for (d of result.dates; track d) { <span style="margin-right: 10px">{{ d | date: 'EEE d MMM' }}</span> }
        </div>
      }
    </div>

    <div class="card scroll">
      <div class="row spread">
        <h2>Monthly summary</h2>
        <div class="row">
          <select class="inline" [(ngModel)]="month" (ngModelChange)="loadSummary()">
            @for (m of monthNames; track m; let i = $index) { <option [ngValue]="i + 1">{{ m }}</option> }
          </select>
          <input type="number" class="inline" style="width: 90px" [(ngModel)]="year" (ngModelChange)="loadSummary()" />
        </div>
      </div>
      <table>
        <thead><tr><th>Code</th><th>Name</th><th>Weekly off</th><th class="num">Days in month</th></tr></thead>
        <tbody>
          @for (r of summary; track r.employeeId) {
            <tr><td>{{ r.employeeCode }}</td><td>{{ r.fullName }}</td><td>{{ r.weeklyOffDays }}</td><td class="num">{{ r.count }}</td></tr>
          } @empty { <tr><td colspan="4" class="empty">No employees.</td></tr> }
        </tbody>
      </table>
    </div>
  `
})
export class WeeklyOffComponent implements OnInit {
  private api = inject(Api);
  private n = inject(Notify);
  days = WEEK_DAYS;
  monthNames = MONTH_NAMES;

  employees: any[] = [];
  employeeId: number | null = null;
  off: Record<string, boolean> = {};

  year = new Date().getFullYear();
  month = new Date().getMonth() + 1;
  from = monthStart(this.year, this.month);
  to = monthEnd(this.year, this.month);
  result: any = null;
  summary: any[] = [];

  async ngOnInit() {
    this.employees = (await this.api.get<any[]>('employees', { activeOnly: true })) ?? [];
    this.employeeId = this.employees[0]?.id ?? null;
    this.onEmployee();
    await this.loadSummary();
  }

  onEmployee() {
    const e = this.employees.find(x => x.id === this.employeeId);
    const set = (e?.weeklyOffDays ?? '').split(',').map((x: string) => x.trim());
    this.off = {};
    for (const d of this.days) this.off[d] = set.includes(d);
    this.result = null;
  }

  async save() {
    const chosen = this.days.filter(d => this.off[d]);
    const res: any = await this.api.put(`weeklyoff/${this.employeeId}`, { days: chosen });
    if (!res) return;
    const e = this.employees.find(x => x.id === this.employeeId);
    if (e) e.weeklyOffDays = res.weeklyOffDays;
    this.n.ok('Saved. Re-run attendance processing for periods already processed.');
    await this.loadSummary();
  }

  async calculate() {
    if (!this.employeeId) return;
    this.result = await this.api.get(`weeklyoff/${this.employeeId}`, { from: this.from, to: this.to });
  }

  async loadSummary() {
    this.summary = (await this.api.get<any[]>('weeklyoff/summary', { year: this.year, month: this.month })) ?? [];
  }
}

import { Component, OnInit, inject } from '@angular/core';
import { DatePipe, DecimalPipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Api, Notify } from '../core/api.service';
import { LEAVE_TYPES, badge, nz, today } from '../core/util';

@Component({
  selector: 'app-leave',
  standalone: true,
  imports: [FormsModule, DatePipe, DecimalPipe],
  template: `
    <h1>Leave</h1>
    <p class="sub">Casual, sick, earned and other leave: balances, applications and approval.</p>

    <div class="tabs">
      @for (t of tabs; track t.id) { <button [class.on]="tab === t.id" (click)="setTab(t.id)">{{ t.label }}</button> }
    </div>

    @if (tab === 'balances' || tab === 'apply') {
      <div class="card row">
        <div class="grow"><label>Employee</label>
          <select [(ngModel)]="employeeId" (ngModelChange)="onEmployee()">
            @for (e of employees; track e.id) { <option [ngValue]="e.id">{{ e.employeeCode }} - {{ e.fullName }}</option> }
          </select></div>
        <div><label>Year</label><input type="number" class="inline" style="width: 100px" [(ngModel)]="year" (ngModelChange)="loadBalances()" /></div>
      </div>
    }

    @if (tab === 'balances') {
      <div class="card scroll">
        <table>
          <thead><tr><th>Leave type</th><th class="num">Entitled</th><th class="num">Used</th><th class="num">Balance</th><th>Notes</th></tr></thead>
          <tbody>
            @for (b of balances; track b.leaveType) {
              <tr>
                <td><b>{{ b.leaveType }}</b></td>
                <td class="num">{{ b.leaveType === 'Unpaid' ? '-' : (b.entitled | number: '1.0-1') }}</td>
                <td class="num">{{ b.used | number: '1.0-1' }}</td>
                <td class="num"><b>{{ b.leaveType === 'Unpaid' ? '-' : (b.balance | number: '1.0-1') }}</b></td>
                <td class="muted small">{{ b.note }}
                  @if (b.leaveType === 'Earned' && b.accruingThisYear > 0) { <div>Accruing this year: {{ b.accruingThisYear }} day(s)</div> }</td>
              </tr>
            } @empty { <tr><td colspan="5" class="empty">Select an employee.</td></tr> }
          </tbody>
        </table>
      </div>
    }

    @if (tab === 'apply') {
      <div class="card">
        <h2>Apply for leave</h2>
        <div class="grid">
          <div><label>Leave type</label>
            <select [(ngModel)]="a.leaveType" (ngModelChange)="preview()">@for (t of types; track t) { <option [value]="t">{{ t }}</option> }</select></div>
          <div><label>From</label><input type="date" [(ngModel)]="a.fromDate" (ngModelChange)="preview()" /></div>
          <div><label>To</label><input type="date" [(ngModel)]="a.toDate" (ngModelChange)="preview()" /></div>
          <div><label>Days counted</label><input [value]="days === null ? '' : days" disabled /></div>
          <div class="span2"><label>Reason</label><input [(ngModel)]="a.reason" /></div>
        </div>
        <p class="muted small">Weekly off days and public holidays inside the range are not counted (except maternity and paternity leave).</p>
        <button (click)="apply()">Submit application</button>
      </div>
    }

    @if (tab === 'requests') {
      <div class="card row">
        <div><label>Status</label>
          <select class="inline" [(ngModel)]="statusFilter" (ngModelChange)="loadRequests()">
            <option value="">All</option><option>Pending</option><option>Approved</option><option>Rejected</option><option>Cancelled</option>
          </select></div>
      </div>
      <div class="card scroll">
        <table>
          <thead><tr><th>Employee</th><th>Type</th><th>From</th><th>To</th><th class="num">Days</th><th>Reason</th><th>Status</th><th></th></tr></thead>
          <tbody>
            @for (r of requests; track r.id) {
              <tr>
                <td>{{ r.employeeCode }} {{ r.employeeName }}</td><td>{{ r.leaveType }}</td>
                <td>{{ r.fromDate | date: 'd MMM yyyy' }}</td><td>{{ r.toDate | date: 'd MMM yyyy' }}</td>
                <td class="num">{{ r.days }}</td><td>{{ r.reason }}</td>
                <td><span class="badge {{ badge(r.status) }}">{{ r.status }}</span></td>
                <td><div class="actions">
                  @if (r.status === 'Pending') {
                    <button class="sm" (click)="decide(r, true)">Approve</button>
                    <button class="sm danger" (click)="decide(r, false)">Reject</button>
                  }
                  @if (r.status === 'Pending' || r.status === 'Approved') { <button class="sm ghost" (click)="cancel(r)">Cancel</button> }
                </div></td>
              </tr>
            } @empty { <tr><td colspan="8" class="empty">No leave applications.</td></tr> }
          </tbody>
        </table>
      </div>
    }

    @if (tab === 'policies') {
      <div class="card scroll">
        <p class="muted small">Policies drive every balance calculation. Earned leave: 1 day per N days worked, usable after the eligibility period, up to the accumulation cap.</p>
        <table>
          <thead><tr><th>Leave type</th><th>Days / year</th><th>Work days per leave</th><th>Max accumulate</th><th>Eligible after (months)</th><th>Prorate</th><th></th></tr></thead>
          <tbody>
            @for (p of policies; track p.id) {
              <tr>
                <td><b>{{ p.leaveType }}</b><div class="muted small">{{ p.description }}</div></td>
                <td><input type="number" min="0" [(ngModel)]="p.daysPerYear" style="width: 90px" /></td>
                <td><input type="number" min="0" [(ngModel)]="p.workDaysPerLeave" style="width: 90px" /></td>
                <td><input type="number" min="0" [(ngModel)]="p.maxAccumulate" style="width: 90px" /></td>
                <td><input type="number" min="0" [(ngModel)]="p.eligibleAfterMonths" style="width: 90px" /></td>
                <td><input type="checkbox" [(ngModel)]="p.proRateFirstYear" /></td>
                <td><button class="sm" (click)="savePolicy(p)">Save</button></td>
              </tr>
            }
          </tbody>
        </table>
      </div>
    }
  `
})
export class LeaveComponent implements OnInit {
  private api = inject(Api);
  private n = inject(Notify);
  badge = badge;
  types = LEAVE_TYPES;
  tabs = [
    { id: 'balances', label: 'Balances' }, { id: 'apply', label: 'Apply' },
    { id: 'requests', label: 'Requests' }, { id: 'policies', label: 'Policies' }
  ];
  tab = 'balances';

  employees: any[] = [];
  employeeId: number | null = null;
  year = new Date().getFullYear();
  balances: any[] = [];
  requests: any[] = [];
  policies: any[] = [];
  statusFilter = 'Pending';

  a: any = { leaveType: 'Casual', fromDate: today(), toDate: today(), reason: '' };
  days: number | null = null;

  async ngOnInit() {
    this.employees = (await this.api.get<any[]>('employees', { activeOnly: true })) ?? [];
    this.employeeId = this.employees[0]?.id ?? null;
    await this.onEmployee();
  }

  async setTab(t: string) {
    this.tab = t;
    if (t === 'requests') await this.loadRequests();
    if (t === 'policies') this.policies = (await this.api.get<any[]>('leave/policies')) ?? [];
    if (t === 'apply') await this.preview();
  }

  async onEmployee() {
    await this.loadBalances();
    await this.preview();
  }

  async loadBalances() {
    this.balances = this.employeeId ? ((await this.api.get<any[]>('leave/balances', { employeeId: this.employeeId, year: this.year })) ?? []) : [];
  }

  async preview() {
    this.days = null;
    if (!this.employeeId || !this.a.fromDate || !this.a.toDate || this.a.toDate < this.a.fromDate) return;
    const r: any = await this.api.get('leave/calculate',
      { employeeId: this.employeeId, type: this.a.leaveType, from: this.a.fromDate, to: this.a.toDate });
    this.days = r ? r.days : null;
  }

  async apply() {
    if (!this.employeeId) return;
    const res = await this.api.post('leave/apply', {
      employeeId: this.employeeId, leaveType: this.a.leaveType, fromDate: this.a.fromDate, toDate: this.a.toDate,
      reason: nz(this.a.reason)
    });
    if (!res) return;
    this.n.ok('Leave application submitted for approval.');
    this.a.reason = '';
    await this.loadBalances();
  }

  async loadRequests() {
    this.requests = (await this.api.get<any[]>('leave/applications', { status: this.statusFilter })) ?? [];
  }

  async decide(r: any, approve: boolean) {
    const remarks = approve ? null : prompt('Reason for rejection (optional)');
    if (!approve && remarks === null) return;
    if (await this.api.post(`leave/applications/${r.id}/decision`, { approve, remarks: nz(remarks) })) {
      this.n.ok(approve ? 'Leave approved.' : 'Leave rejected.');
      await this.loadRequests();
    }
  }

  async cancel(r: any) {
    if (!confirm('Cancel this leave application?')) return;
    if (await this.api.post(`leave/applications/${r.id}/cancel`)) { this.n.ok('Cancelled.'); await this.loadRequests(); }
  }

  async savePolicy(p: any) {
    const body = {
      daysPerYear: p.daysPerYear ?? 0, workDaysPerLeave: p.workDaysPerLeave ?? 0, maxAccumulate: p.maxAccumulate ?? 0,
      eligibleAfterMonths: p.eligibleAfterMonths ?? 0, proRateFirstYear: !!p.proRateFirstYear
    };
    if (await this.api.put(`leave/policies/${p.id}`, body)) this.n.ok(`${p.leaveType} policy saved.`);
  }
}

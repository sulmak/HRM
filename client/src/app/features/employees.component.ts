import { Component, OnInit, inject } from '@angular/core';
import { DatePipe, DecimalPipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Api, Notify } from '../core/api.service';
import { EMPLOYMENT_STATUS, GENDERS, WEEK_DAYS, badge, nz, today } from '../core/util';

@Component({
  selector: 'app-employees',
  standalone: true,
  imports: [FormsModule, DatePipe, DecimalPipe],
  template: `
    <div class="row spread">
      <div><h1>Employees</h1><p class="sub">Employee information, weekly off days and salary structure.</p></div>
      <button (click)="newEmployee()">Add employee</button>
    </div>

    @if (showForm) {
      <div class="card">
        <h2>{{ editId ? 'Edit ' + f.employeeCode : 'New employee' }}</h2>
        <div class="grid">
          <div class="span2"><label>Full name</label><input [(ngModel)]="f.fullName" /></div>
          <div><label>Email</label><input type="email" [(ngModel)]="f.email" /></div>
          <div><label>Phone</label><input [(ngModel)]="f.phone" /></div>
          <div><label>Gender</label><select [(ngModel)]="f.gender">@for (g of genders; track g) { <option [value]="g">{{ g }}</option> }</select></div>
          <div><label>Date of birth</label><input type="date" [(ngModel)]="f.dateOfBirth" /></div>
          <div><label>National ID</label><input [(ngModel)]="f.nationalId" /></div>
          <div class="span2"><label>Address</label><input [(ngModel)]="f.address" /></div>
          <div><label>Department</label><select [(ngModel)]="f.departmentId"><option [ngValue]="null">-</option>
            @for (d of lookups.departments; track d.id) { <option [ngValue]="d.id">{{ d.name }}</option> }</select></div>
          <div><label>Designation</label><select [(ngModel)]="f.designationId"><option [ngValue]="null">-</option>
            @for (d of lookups.designations; track d.id) { <option [ngValue]="d.id">{{ d.name }}</option> }</select></div>
          <div><label>Shift</label><select [(ngModel)]="f.shiftId"><option [ngValue]="null">-</option>
            @for (s of lookups.shifts; track s.id) { <option [ngValue]="s.id">{{ s.name }}</option> }</select></div>
          <div><label>Reports to</label><select [(ngModel)]="f.reportsToId"><option [ngValue]="null">-</option>
            @for (e of list; track e.id) { @if (e.id !== editId) { <option [ngValue]="e.id">{{ e.fullName }}</option> } }</select></div>
          <div><label>Joining date</label><input type="date" [(ngModel)]="f.joiningDate" /></div>
          <div><label>Confirmation date</label><input type="date" [(ngModel)]="f.confirmationDate" /></div>
          <div><label>Last working day</label><input type="date" [(ngModel)]="f.lastWorkingDate" /></div>
          <div><label>Status</label><select [(ngModel)]="f.status">@for (s of statuses; track s) { <option [value]="s">{{ s }}</option> }</select></div>
          <div><label>Opening earned leave (days)</label><input type="number" min="0" [(ngModel)]="f.openingEarnLeave" /></div>
          <div class="span2"><label>Weekly off days</label>
            @for (d of days; track d) {
              <label class="chk"><input type="checkbox" [(ngModel)]="off[d]" /> {{ d }}</label>
            }</div>
        </div>
        <div class="row" style="margin-top: 12px">
          <button (click)="save()">Save</button>
          <button class="ghost" (click)="showForm = false">Cancel</button>
        </div>
      </div>
    }

    @if (salaryFor) {
      <div class="panel">
        <h2>Salary structure &mdash; {{ salaryFor.fullName }}</h2>
        <div class="grid">
          <div><label>Basic</label><input type="number" min="0" [(ngModel)]="s.basic" /></div>
          <div><label>House rent</label><input type="number" min="0" [(ngModel)]="s.houseRent" /></div>
          <div><label>Medical</label><input type="number" min="0" [(ngModel)]="s.medical" /></div>
          <div><label>Conveyance</label><input type="number" min="0" [(ngModel)]="s.conveyance" /></div>
          <div><label>Other allowance</label><input type="number" min="0" [(ngModel)]="s.otherAllowance" /></div>
          <div><label>Provident fund (% of basic)</label><input type="number" min="0" [(ngModel)]="s.providentFundPercent" /></div>
          <div><label>Monthly tax</label><input type="number" min="0" [(ngModel)]="s.monthlyTax" /></div>
          <div><label>Effective from</label><input type="date" [(ngModel)]="s.effectiveFrom" /></div>
        </div>
        <p><b>Gross: {{ gross() | number: '1.2-2' }}</b></p>
        @if (history.length) {
          <div class="muted small">History:
            @for (h of history; track h.id) { <span style="margin-right: 14px">{{ h.effectiveFrom | date: 'd MMM yyyy' }}: {{ h.gross | number: '1.0-0' }}</span> }
          </div>
        }
        <div class="row" style="margin-top: 10px">
          <button (click)="saveSalary()">Save salary</button>
          <button class="ghost" (click)="salaryFor = null">Close</button>
        </div>
      </div>
    }

    <div class="card">
      <div class="row">
        <div class="grow"><label>Search</label><input [(ngModel)]="search" (ngModelChange)="load()" placeholder="Name, code or email" /></div>
        <div><label>Department</label><select class="inline" [(ngModel)]="deptFilter" (ngModelChange)="load()"><option [ngValue]="null">All</option>
          @for (d of lookups.departments; track d.id) { <option [ngValue]="d.id">{{ d.name }}</option> }</select></div>
      </div>
    </div>

    <div class="card scroll">
      <table>
        <thead><tr><th>Code</th><th>Name</th><th>Department</th><th>Designation</th><th>Joined</th><th>Weekly off</th><th>Status</th><th></th></tr></thead>
        <tbody>
          @for (e of list; track e.id) {
            <tr>
              <td>{{ e.employeeCode }}</td>
              <td><b>{{ e.fullName }}</b><div class="muted small">{{ e.email }}</div></td>
              <td>{{ e.department }}</td><td>{{ e.designation }}</td>
              <td>{{ e.joiningDate | date: 'd MMM yyyy' }}</td>
              <td>{{ e.weeklyOffDays }}</td>
              <td><span class="badge {{ badge(e.status) }}">{{ e.status }}</span></td>
              <td><div class="actions">
                <button class="sm ghost" (click)="edit(e)">Edit</button>
                <button class="sm ghost" (click)="openSalary(e)">Salary</button>
              </div></td>
            </tr>
          } @empty { <tr><td colspan="8" class="empty">No employees found.</td></tr> }
        </tbody>
      </table>
    </div>
  `
})
export class EmployeesComponent implements OnInit {
  private api = inject(Api);
  private n = inject(Notify);
  badge = badge;
  genders = GENDERS;
  statuses = EMPLOYMENT_STATUS;
  days = WEEK_DAYS;

  list: any[] = [];
  lookups: any = { departments: [], designations: [], shifts: [] };
  search = '';
  deptFilter: number | null = null;

  showForm = false;
  editId: number | null = null;
  f: any = {};
  off: Record<string, boolean> = {};

  salaryFor: any = null;
  s: any = {};
  history: any[] = [];

  async ngOnInit() {
    this.lookups = (await this.api.get('lookups')) ?? this.lookups;
    await this.load();
  }

  async load() {
    this.list = (await this.api.get<any[]>('employees', { search: this.search, departmentId: this.deptFilter })) ?? [];
  }

  blank() {
    return { fullName: '', email: '', phone: '', gender: 'Male', dateOfBirth: '', nationalId: '', address: '',
      departmentId: null, designationId: null, shiftId: this.lookups.shifts[0]?.id ?? null, reportsToId: null,
      joiningDate: today(), confirmationDate: '', lastWorkingDate: '', status: 'Probation', openingEarnLeave: 0 };
  }

  setOff(csv: string) {
    this.off = {};
    for (const d of this.days) this.off[d] = csv.split(',').map(x => x.trim()).includes(d);
  }

  newEmployee() {
    this.editId = null;
    this.f = this.blank();
    this.setOff('Friday');
    this.showForm = true;
  }

  edit(e: any) {
    this.editId = e.id;
    this.f = { ...e };
    this.setOff(e.weeklyOffDays ?? '');
    this.showForm = true;
  }

  async save() {
    const f = this.f;
    if (!f.fullName?.trim()) { this.n.error('Full name is required.'); return; }
    const body = {
      fullName: f.fullName, email: nz(f.email), phone: nz(f.phone), gender: f.gender, dateOfBirth: nz(f.dateOfBirth),
      nationalId: nz(f.nationalId), address: nz(f.address), departmentId: nz(f.departmentId),
      designationId: nz(f.designationId), shiftId: nz(f.shiftId), reportsToId: nz(f.reportsToId),
      joiningDate: f.joiningDate, confirmationDate: nz(f.confirmationDate), lastWorkingDate: nz(f.lastWorkingDate),
      status: f.status, openingEarnLeave: f.openingEarnLeave ?? 0,
      weeklyOffDays: this.days.filter(d => this.off[d]).join(',')
    };
    const res = this.editId ? await this.api.put(`employees/${this.editId}`, body) : await this.api.post('employees', body);
    if (!res) return;
    this.n.ok('Employee saved.');
    this.showForm = false;
    await this.load();
  }

  async openSalary(e: any) {
    this.salaryFor = e;
    this.history = (await this.api.get<any[]>(`payroll/structures/${e.id}`)) ?? [];
    const cur = this.history[0];
    this.s = cur
      ? { ...cur, effectiveFrom: today() }
      : { basic: 0, houseRent: 0, medical: 0, conveyance: 0, otherAllowance: 0, providentFundPercent: 0, monthlyTax: 0, effectiveFrom: e.joiningDate };
  }

  gross() {
    const s = this.s;
    return (+s.basic || 0) + (+s.houseRent || 0) + (+s.medical || 0) + (+s.conveyance || 0) + (+s.otherAllowance || 0);
  }

  async saveSalary() {
    const s = this.s;
    const body = {
      basic: +s.basic || 0, houseRent: +s.houseRent || 0, medical: +s.medical || 0, conveyance: +s.conveyance || 0,
      otherAllowance: +s.otherAllowance || 0, providentFundPercent: +s.providentFundPercent || 0,
      monthlyTax: +s.monthlyTax || 0, effectiveFrom: s.effectiveFrom
    };
    if (await this.api.put(`payroll/structures/${this.salaryFor.id}`, body)) {
      this.n.ok('Salary structure saved.');
      this.history = (await this.api.get<any[]>(`payroll/structures/${this.salaryFor.id}`)) ?? [];
    }
  }
}

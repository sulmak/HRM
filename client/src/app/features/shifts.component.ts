import { Component, OnInit, inject } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Api, Notify } from '../core/api.service';
import { hm, toTime } from '../core/util';

@Component({
  selector: 'app-shifts',
  standalone: true,
  imports: [FormsModule],
  template: `
    <div class="row spread">
      <div><h1>Shifts</h1><p class="sub">Define shifts, then move employees between them.</p></div>
      <button (click)="newShift()">Add shift</button>
    </div>

    @if (showForm) {
      <div class="card">
        <h2>{{ editId ? 'Edit shift' : 'New shift' }}</h2>
        <div class="grid">
          <div class="span2"><label>Name</label><input [(ngModel)]="f.name" placeholder="e.g. Morning (07:00 - 15:00)" /></div>
          <div><label>Start time</label><input type="time" [(ngModel)]="f.startTime" /></div>
          <div><label>End time</label><input type="time" [(ngModel)]="f.endTime" /></div>
          <div><label>Late grace (minutes)</label><input type="number" min="0" [(ngModel)]="f.graceMinutes" /></div>
          <div><label>Half-day below (worked hours)</label><input type="number" min="0" step="0.5" [(ngModel)]="f.halfDayHours" /></div>
        </div>
        <p class="muted small">An end time earlier than the start time is treated as an overnight shift.</p>
        <div class="row">
          <button (click)="save()">Save</button>
          <button class="ghost" (click)="showForm = false">Cancel</button>
        </div>
      </div>
    }

    <div class="card scroll">
      <table>
        <thead><tr><th>Shift</th><th>Start</th><th>End</th><th class="num">Grace (min)</th><th class="num">Half-day below (h)</th><th class="num">Employees</th><th></th></tr></thead>
        <tbody>
          @for (s of shifts; track s.id) {
            <tr>
              <td><b>{{ s.name }}</b> @if (overnight(s)) { <span class="badge b-blue">Overnight</span> }</td>
              <td>{{ hm(s.startTime) }}</td><td>{{ hm(s.endTime) }}</td>
              <td class="num">{{ s.graceMinutes }}</td><td class="num">{{ s.halfDayHours }}</td>
              <td class="num">{{ s.employees }}</td>
              <td><div class="actions">
                <button class="sm ghost" (click)="edit(s)">Edit</button>
                <button class="sm ghost" (click)="targetShiftId = s.id">Assign employees</button>
                <button class="sm danger" (click)="remove(s)">Delete</button>
              </div></td>
            </tr>
          } @empty { <tr><td colspan="7" class="empty">No shifts yet.</td></tr> }
        </tbody>
      </table>
    </div>

    <div class="card">
      <h2>Shift change</h2>
      <div class="row">
        <div class="grow"><label>Move selected employees to</label>
          <select [(ngModel)]="targetShiftId">
            <option [ngValue]="null">Choose a shift</option>
            @for (s of shifts; track s.id) { <option [ngValue]="s.id">{{ s.name }}</option> }
          </select></div>
        <div><label>Department</label>
          <select class="inline" [(ngModel)]="deptFilter">
            <option [ngValue]="null">All</option>
            @for (d of departments; track d.id) { <option [ngValue]="d.id">{{ d.name }}</option> }
          </select></div>
        <div><label>Search</label><input class="inline" [(ngModel)]="search" placeholder="Name or code" /></div>
        <button [disabled]="!selected.size || !targetShiftId" (click)="assign()">Apply to {{ selected.size }} employee(s)</button>
      </div>
      <p class="muted small">Attendance processing uses an employee's current shift. After a shift change, re-run Attendance &rarr; Process for dates already processed (months with a finalized payroll stay locked).</p>

      <div class="scroll">
        <table>
          <thead><tr>
            <th style="width: 34px"><input type="checkbox" [checked]="allShownSelected()" (change)="toggleAll($event)" /></th>
            <th>Code</th><th>Name</th><th>Department</th><th>Current shift</th>
          </tr></thead>
          <tbody>
            @for (e of shown; track e.id) {
              <tr>
                <td><input type="checkbox" [checked]="selected.has(e.id)" (change)="toggle(e.id)" /></td>
                <td>{{ e.employeeCode }}</td><td>{{ e.fullName }}</td><td>{{ e.department }}</td>
                <td>{{ shiftName(e.shiftId) }}</td>
              </tr>
            } @empty { <tr><td colspan="5" class="empty">No employees match.</td></tr> }
          </tbody>
        </table>
      </div>
    </div>
  `
})
export class ShiftsComponent implements OnInit {
  private api = inject(Api);
  private n = inject(Notify);
  hm = hm;

  shifts: any[] = [];
  employees: any[] = [];
  departments: any[] = [];

  showForm = false;
  editId: number | null = null;
  f: any = {};

  targetShiftId: number | null = null;
  deptFilter: number | null = null;
  search = '';
  selected = new Set<number>();

  async ngOnInit() {
    const lookups: any = await this.api.get('lookups');
    this.departments = lookups?.departments ?? [];
    await this.load();
  }

  async load() {
    this.shifts = (await this.api.get<any[]>('shifts')) ?? [];
    this.employees = (await this.api.get<any[]>('employees', { activeOnly: true })) ?? [];
  }

  get shown() {
    const q = this.search.trim().toLowerCase();
    return this.employees.filter(e =>
      (this.deptFilter === null || e.departmentId === this.deptFilter) &&
      (!q || e.fullName.toLowerCase().includes(q) || e.employeeCode.toLowerCase().includes(q)));
  }

  shiftName(id: number | null) { return this.shifts.find(s => s.id === id)?.name ?? '-'; }
  overnight(s: any) { return s.endTime < s.startTime; }

  toggle(id: number) {
    if (this.selected.has(id)) this.selected.delete(id); else this.selected.add(id);
  }

  allShownSelected() { return this.shown.length > 0 && this.shown.every(e => this.selected.has(e.id)); }

  toggleAll(ev: Event) {
    const on = (ev.target as HTMLInputElement).checked;
    for (const e of this.shown) { if (on) this.selected.add(e.id); else this.selected.delete(e.id); }
  }

  newShift() {
    this.editId = null;
    this.f = { name: '', startTime: '09:00', endTime: '18:00', graceMinutes: 10, halfDayHours: 4 };
    this.showForm = true;
  }

  edit(s: any) {
    this.editId = s.id;
    this.f = { ...s, startTime: hm(s.startTime), endTime: hm(s.endTime) };
    this.showForm = true;
  }

  async save() {
    const f = this.f;
    if (!f.name?.trim()) { this.n.error('Shift name is required.'); return; }
    if (!f.startTime || !f.endTime) { this.n.error('Start and end time are required.'); return; }
    const body = {
      name: f.name, startTime: toTime(f.startTime), endTime: toTime(f.endTime),
      graceMinutes: +f.graceMinutes || 0, halfDayHours: +f.halfDayHours || 0
    };
    const res = this.editId ? await this.api.put(`shifts/${this.editId}`, body) : await this.api.post('shifts', body);
    if (!res) return;
    this.n.ok('Shift saved. Re-run attendance processing if past dates should use the new times.');
    this.showForm = false;
    await this.load();
  }

  async remove(s: any) {
    if (!confirm(`Delete shift "${s.name}"?`)) return;
    if (await this.api.delete(`shifts/${s.id}`)) { this.n.ok('Shift deleted.'); await this.load(); }
  }

  async assign() {
    if (!this.targetShiftId || !this.selected.size) return;
    const name = this.shiftName(this.targetShiftId);
    if (!confirm(`Move ${this.selected.size} employee(s) to "${name}"?`)) return;
    const res: any = await this.api.post('shifts/assign', { shiftId: this.targetShiftId, employeeIds: [...this.selected] });
    if (!res) return;
    this.n.ok(`${res.changed} employee(s) moved to ${name}. Re-run attendance processing for dates already processed.`);
    this.selected.clear();
    await this.load();
  }
}

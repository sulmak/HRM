import { Component, OnInit, inject } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Api } from '../core/api.service';
import { MONTH_NAMES, today, ymd } from '../core/util';

interface Cell { day: number; date: string; events: any[]; off: boolean; today: boolean; }

@Component({
  selector: 'app-calendar',
  standalone: true,
  imports: [FormsModule],
  template: `
    <h1>Employee calendar</h1>
    <p class="sub">Holidays, weekly offs, leave, attendance, birthdays and work anniversaries.</p>

    <div class="card row">
      <div class="grow"><label>Employee</label>
        <select [(ngModel)]="employeeId" (ngModelChange)="load()">
          <option [ngValue]="null">Whole company</option>
          @for (e of employees; track e.id) { <option [ngValue]="e.id">{{ e.employeeCode }} - {{ e.fullName }}</option> }
        </select></div>
      <button class="ghost" (click)="shift(-1)">&laquo; Prev</button>
      <b style="padding: 8px 6px; min-width: 130px; text-align: center">{{ monthNames[month - 1] }} {{ year }}</b>
      <button class="ghost" (click)="shift(1)">Next &raquo;</button>
    </div>

    <div class="card">
      <div class="cal">
        @for (h of heads; track h) { <div class="h">{{ h }}</div> }
        @for (c of cells; track $index) {
          @if (c) {
            <div class="d" [class.today]="c.today" [class.off]="c.off">
              <div class="n">{{ c.day }}</div>
              @for (e of c.events; track $index) {
                <div class="ev {{ e.type }}" [title]="e.detail ?? ''">{{ e.title }}{{ e.detail && e.type !== 'Attendance' ? ' (' + e.detail + ')' : '' }}{{ e.type === 'Attendance' && e.detail ? ' ' + e.detail : '' }}</div>
              }
            </div>
          } @else { <div class="d blank"></div> }
        }
      </div>
    </div>
  `
})
export class CalendarComponent implements OnInit {
  private api = inject(Api);
  monthNames = MONTH_NAMES;
  heads = ['Sun', 'Mon', 'Tue', 'Wed', 'Thu', 'Fri', 'Sat'];

  employees: any[] = [];
  employeeId: number | null = null;
  year = new Date().getFullYear();
  month = new Date().getMonth() + 1;
  cells: (Cell | null)[] = [];

  async ngOnInit() {
    this.employees = (await this.api.get<any[]>('employees', { activeOnly: true })) ?? [];
    await this.load();
  }

  shift(delta: number) {
    const d = new Date(this.year, this.month - 1 + delta, 1);
    this.year = d.getFullYear();
    this.month = d.getMonth() + 1;
    this.load();
  }

  async load() {
    const events = (await this.api.get<any[]>('calendar', { year: this.year, month: this.month, employeeId: this.employeeId })) ?? [];
    const byDate = new Map<string, any[]>();
    for (const e of events) {
      const key = String(e.date).slice(0, 10);
      byDate.set(key, [...(byDate.get(key) ?? []), e]);
    }

    const first = new Date(this.year, this.month - 1, 1);
    const count = new Date(this.year, this.month, 0).getDate();
    const t = today();
    const cells: (Cell | null)[] = [];
    for (let i = 0; i < first.getDay(); i++) cells.push(null);
    for (let day = 1; day <= count; day++) {
      const date = ymd(new Date(this.year, this.month - 1, day));
      const evs = byDate.get(date) ?? [];
      cells.push({
        day, date, events: evs, today: date === t,
        off: evs.some(e => e.type === 'WeeklyOff' || e.type === 'Holiday')
      });
    }
    this.cells = cells;
  }
}

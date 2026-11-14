import { Component, OnInit, inject } from '@angular/core';
import { DatePipe } from '@angular/common';
import { RouterLink } from '@angular/router';
import { Api } from '../core/api.service';
import { MONTH_NAMES } from '../core/util';

@Component({
  selector: 'app-dashboard',
  standalone: true,
  imports: [DatePipe, RouterLink],
  template: `
    <h1>Dashboard</h1>
    <p class="sub">Overview of people, hiring and today's attendance.</p>

    @if (d) {
      <div class="stats">
        <div class="stat"><b>{{ d.employees }}</b><span>Active employees</span></div>
        <div class="stat"><b>{{ d.openCirculars }}</b><span>Open circulars</span></div>
        <div class="stat"><b>{{ d.candidatesInPipeline }}</b><span>Candidates in pipeline</span></div>
        <div class="stat"><b>{{ d.pendingLeaves }}</b><span>Pending leave requests</span></div>
        <div class="stat"><b>{{ d.todayPresent }}</b><span>Present today</span></div>
        <div class="stat"><b>{{ d.todayAbsent }}</b><span>Absent today</span></div>
        <div class="stat"><b>{{ d.todayOnLeave }}</b><span>On leave today</span></div>
      </div>

      <div class="row" style="align-items: stretch">
        <div class="card grow">
          <h2>Upcoming holidays</h2>
          @for (h of d.upcomingHolidays; track h.id) {
            <div class="row spread" style="padding: 4px 0">
              <span>{{ h.name }}</span><span class="muted">{{ h.date | date: 'EEE, d MMM yyyy' }}</span>
            </div>
          } @empty { <div class="empty">No upcoming holidays. Add them through the holidays API.</div> }
        </div>
        <div class="card grow">
          <h2>Latest payroll</h2>
          @if (d.latestPayroll) {
            <p>{{ months[d.latestPayroll.month - 1] }} {{ d.latestPayroll.year }} &middot; <b>{{ d.latestPayroll.status }}</b></p>
            <a routerLink="/payroll">Open payroll</a>
          } @else {
            <div class="empty">No payroll processed yet.</div>
          }
        </div>
      </div>
    }
  `
})
export class DashboardComponent implements OnInit {
  private api = inject(Api);
  d: any = null;
  months = MONTH_NAMES;

  async ngOnInit() {
    this.d = await this.api.get('dashboard');
  }
}

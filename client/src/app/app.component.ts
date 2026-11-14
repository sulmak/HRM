import { Component, inject } from '@angular/core';
import { RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { Notify } from './core/api.service';

@Component({
  selector: 'app-root',
  standalone: true,
  imports: [RouterOutlet, RouterLink, RouterLinkActive],
  template: `
    <div class="shell">
      <aside class="side">
        <div class="brand">HRM</div>
        <nav>
          <a routerLink="/dashboard" routerLinkActive="on">Dashboard</a>

          <div class="grp">Recruitment</div>
          <a routerLink="/recruitment/circulars" routerLinkActive="on">Circulars</a>
          <a routerLink="/recruitment/cv-drop" routerLinkActive="on">CV drop</a>
          <a routerLink="/recruitment/pipeline" routerLinkActive="on">Selection pipeline</a>

          <div class="grp">People</div>
          <a routerLink="/employees" routerLinkActive="on">Employees</a>
          <a routerLink="/calendar" routerLinkActive="on">Calendar</a>
          <a routerLink="/performance" routerLinkActive="on">Performance</a>

          <div class="grp">Time &amp; pay</div>
          <a routerLink="/leave" routerLinkActive="on">Leave</a>
          <a routerLink="/shifts" routerLinkActive="on">Shifts</a>
          <a routerLink="/weekly-off" routerLinkActive="on">Weekly off</a>
          <a routerLink="/attendance" routerLinkActive="on">Attendance</a>
          <a routerLink="/payroll" routerLinkActive="on">Payroll</a>
        </nav>
        <div class="credit">Powered by<br>Md. Atikur Rahman Mridha</div>
      </aside>
      <main class="main"><router-outlet /></main>
    </div>
    @if (notify.toast(); as t) {
      <div class="toast" [class.err]="t.kind === 'error'" (click)="notify.toast.set(null)">{{ t.text }}</div>
    }
  `
})
export class AppComponent {
  notify = inject(Notify);
}

import { Routes } from '@angular/router';

export const routes: Routes = [
  { path: '', pathMatch: 'full', redirectTo: 'dashboard' },
  { path: 'dashboard', loadComponent: () => import('./features/dashboard.component').then(m => m.DashboardComponent) },
  { path: 'recruitment/circulars', loadComponent: () => import('./features/circulars.component').then(m => m.CircularsComponent) },
  { path: 'recruitment/cv-drop', loadComponent: () => import('./features/cv-drop.component').then(m => m.CvDropComponent) },
  { path: 'recruitment/pipeline', loadComponent: () => import('./features/pipeline.component').then(m => m.PipelineComponent) },
  { path: 'employees', loadComponent: () => import('./features/employees.component').then(m => m.EmployeesComponent) },
  { path: 'leave', loadComponent: () => import('./features/leave.component').then(m => m.LeaveComponent) },
  { path: 'shifts', loadComponent: () => import('./features/shifts.component').then(m => m.ShiftsComponent) },
  { path: 'weekly-off', loadComponent: () => import('./features/weekly-off.component').then(m => m.WeeklyOffComponent) },
  { path: 'calendar', loadComponent: () => import('./features/calendar.component').then(m => m.CalendarComponent) },
  { path: 'attendance', loadComponent: () => import('./features/attendance.component').then(m => m.AttendanceComponent) },
  { path: 'payroll', loadComponent: () => import('./features/payroll.component').then(m => m.PayrollComponent) },
  { path: 'performance', loadComponent: () => import('./features/performance.component').then(m => m.PerformanceComponent) },
  { path: '**', redirectTo: 'dashboard' }
];

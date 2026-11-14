import { Component, OnInit, inject } from '@angular/core';
import { DatePipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { Api, Notify } from '../core/api.service';
import { badge, nz, today } from '../core/util';

@Component({
  selector: 'app-circulars',
  standalone: true,
  imports: [FormsModule, DatePipe, RouterLink],
  template: `
    <div class="row spread">
      <div><h1>Job circulars</h1><p class="sub">Step 1 of the selection process: create and publish a circular.</p></div>
      <button (click)="toggle()">{{ showForm ? 'Close form' : 'New circular' }}</button>
    </div>

    @if (showForm) {
      <div class="card">
        <h2>{{ editId ? 'Edit draft circular' : 'New circular' }}</h2>
        <div class="grid">
          <div class="span2"><label>Title</label><input [(ngModel)]="f.title" /></div>
          <div><label>Department</label>
            <select [(ngModel)]="f.departmentId"><option [ngValue]="null">-</option>
              @for (d of lookups.departments; track d.id) { <option [ngValue]="d.id">{{ d.name }}</option> }</select></div>
          <div><label>Designation</label>
            <select [(ngModel)]="f.designationId"><option [ngValue]="null">-</option>
              @for (d of lookups.designations; track d.id) { <option [ngValue]="d.id">{{ d.name }}</option> }</select></div>
          <div><label>Vacancies</label><input type="number" min="1" [(ngModel)]="f.vacancies" /></div>
          <div><label>Application deadline</label><input type="date" [(ngModel)]="f.deadline" /></div>
          <div><label>Location</label><input [(ngModel)]="f.location" /></div>
          <div><label>Salary range</label><input [(ngModel)]="f.salaryRange" placeholder="e.g. 40,000 - 55,000" /></div>
          <div class="span2"><label>Description</label><textarea [(ngModel)]="f.description"></textarea></div>
          <div class="span2"><label>Requirements</label><textarea [(ngModel)]="f.requirements"></textarea></div>
        </div>
        <div class="row" style="margin-top: 12px">
          <button (click)="save()">Save as draft</button>
          <button class="ghost" (click)="toggle()">Cancel</button>
        </div>
      </div>
    }

    <div class="card scroll">
      <table>
        <thead><tr><th>Title</th><th>Department</th><th>Vacancies</th><th>Deadline</th><th>Applications</th><th>Status</th><th></th></tr></thead>
        <tbody>
          @for (c of list; track c.id) {
            <tr>
              <td><b>{{ c.title }}</b><div class="muted small">{{ c.designation }} {{ c.location ? '· ' + c.location : '' }}</div></td>
              <td>{{ c.department }}</td>
              <td>{{ c.filled }} / {{ c.vacancies }} filled</td>
              <td>{{ c.deadline | date: 'd MMM yyyy' }}</td>
              <td>{{ c.applications }}</td>
              <td><span class="badge {{ badge(c.status) }}">{{ c.status }}</span></td>
              <td>
                <div class="actions">
                  @if (c.status === 'Draft') {
                    <button class="sm" (click)="publish(c)">Publish</button>
                    <button class="sm ghost" (click)="edit(c)">Edit</button>
                  }
                  @if (c.status === 'Published') { <button class="sm ghost" (click)="close(c)">Close</button> }
                  @if (c.status !== 'Draft') {
                    <a routerLink="/recruitment/pipeline" [queryParams]="{ circularId: c.id }"><button class="sm ghost">Candidates</button></a>
                  }
                </div>
              </td>
            </tr>
          } @empty { <tr><td colspan="7" class="empty">No circulars yet.</td></tr> }
        </tbody>
      </table>
    </div>
  `
})
export class CircularsComponent implements OnInit {
  private api = inject(Api);
  private n = inject(Notify);
  badge = badge;
  list: any[] = [];
  lookups: any = { departments: [], designations: [] };
  showForm = false;
  editId: number | null = null;
  f: any = {};

  async ngOnInit() {
    this.lookups = (await this.api.get('lookups')) ?? this.lookups;
    await this.load();
  }

  async load() { this.list = (await this.api.get<any[]>('recruitment/circulars')) ?? []; }

  blank() {
    return { title: '', departmentId: null, designationId: null, vacancies: 1, location: '', description: '',
      requirements: '', salaryRange: '', deadline: today() };
  }

  toggle() {
    this.showForm = !this.showForm;
    this.editId = null;
    this.f = this.blank();
  }

  edit(c: any) {
    this.showForm = true;
    this.editId = c.id;
    this.f = { ...c };
  }

  async save() {
    const body = {
      title: this.f.title, departmentId: nz(this.f.departmentId), designationId: nz(this.f.designationId),
      vacancies: this.f.vacancies, location: nz(this.f.location), description: nz(this.f.description),
      requirements: nz(this.f.requirements), salaryRange: nz(this.f.salaryRange), deadline: this.f.deadline
    };
    const res = this.editId
      ? await this.api.put(`recruitment/circulars/${this.editId}`, body)
      : await this.api.post('recruitment/circulars', body);
    if (!res) return;
    this.n.ok('Circular saved as draft.');
    this.showForm = false;
    await this.load();
  }

  async publish(c: any) {
    if (await this.api.post(`recruitment/circulars/${c.id}/publish`)) { this.n.ok('Circular published.'); await this.load(); }
  }

  async close(c: any) {
    if (!confirm('Close this circular? No more CVs will be accepted.')) return;
    if (await this.api.post(`recruitment/circulars/${c.id}/close`)) { this.n.ok('Circular closed.'); await this.load(); }
  }
}

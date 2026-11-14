import { Component, OnInit, inject } from '@angular/core';
import { DatePipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { Api, Notify } from '../core/api.service';
import { GENDERS } from '../core/util';

@Component({
  selector: 'app-cv-drop',
  standalone: true,
  imports: [FormsModule, DatePipe, RouterLink],
  template: `
    <h1>CV drop</h1>
    <p class="sub">Step 2: record a candidate's application and upload the CV (PDF or Word, up to 5 MB).</p>

    <div class="card">
      @if (!circulars.length) {
        <div class="empty">No circular is open for applications. <a routerLink="/recruitment/circulars">Publish one first.</a></div>
      } @else {
        <div class="grid">
          <div class="span2"><label>Circular</label>
            <select [(ngModel)]="circularId">
              @for (c of circulars; track c.id) { <option [ngValue]="c.id">{{ c.title }} (closes {{ c.deadline | date: 'd MMM' }})</option> }
            </select></div>
          <div><label>Full name</label><input [(ngModel)]="f.fullName" /></div>
          <div><label>Email</label><input type="email" [(ngModel)]="f.email" /></div>
          <div><label>Phone</label><input [(ngModel)]="f.phone" /></div>
          <div><label>Gender</label>
            <select [(ngModel)]="f.gender">@for (g of genders; track g) { <option [value]="g">{{ g }}</option> }</select></div>
          <div><label>Date of birth</label><input type="date" [(ngModel)]="f.dateOfBirth" /></div>
          <div><label>Expected salary</label><input type="number" min="0" [(ngModel)]="f.expectedSalary" /></div>
          <div class="span2"><label>Address</label><input [(ngModel)]="f.address" /></div>
          <div class="span2"><label>CV file</label><input #fileInput type="file" accept=".pdf,.doc,.docx" (change)="onFile($event)" /></div>
        </div>
        <div class="row" style="margin-top: 12px">
          <button [disabled]="busy" (click)="submit(fileInput)">Submit application</button>
        </div>
      }
    </div>
  `
})
export class CvDropComponent implements OnInit {
  private api = inject(Api);
  private n = inject(Notify);
  genders = GENDERS;
  circulars: any[] = [];
  circularId: number | null = null;
  file: File | null = null;
  busy = false;
  f: any = this.blank();

  blank() {
    return { fullName: '', email: '', phone: '', gender: 'Male', dateOfBirth: '', expectedSalary: 0, address: '' };
  }

  async ngOnInit() {
    this.circulars = (await this.api.get<any[]>('recruitment/circulars', { status: 'Published' })) ?? [];
    this.circularId = this.circulars[0]?.id ?? null;
  }

  onFile(e: Event) {
    this.file = (e.target as HTMLInputElement).files?.[0] ?? null;
  }

  async submit(input: HTMLInputElement) {
    if (!this.circularId) return;
    if (!this.f.fullName.trim() || !this.f.email.trim()) { this.n.error('Name and email are required.'); return; }
    const fd = new FormData();
    fd.append('fullName', this.f.fullName);
    fd.append('email', this.f.email);
    fd.append('gender', this.f.gender);
    fd.append('expectedSalary', String(this.f.expectedSalary ?? 0));
    if (this.f.phone) fd.append('phone', this.f.phone);
    if (this.f.dateOfBirth) fd.append('dateOfBirth', this.f.dateOfBirth);
    if (this.f.address) fd.append('address', this.f.address);
    if (this.file) fd.append('cv', this.file);

    this.busy = true;
    const res = await this.api.postForm(`recruitment/circulars/${this.circularId}/apply`, fd);
    this.busy = false;
    if (!res) return;
    this.n.ok('Application recorded.');
    this.f = this.blank();
    this.file = null;
    input.value = '';
  }
}

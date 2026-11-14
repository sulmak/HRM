import { Component, OnInit, inject } from '@angular/core';
import { DecimalPipe, DatePipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Api, Notify } from '../core/api.service';
import { badge, nz, today } from '../core/util';

@Component({
  selector: 'app-performance',
  standalone: true,
  imports: [FormsModule, DecimalPipe, DatePipe],
  template: `
    <h1>Performance</h1>
    <p class="sub">KPI scores (1-5, weighted) blended with attendance discipline into an overall rating.</p>

    <div class="tabs">
      @for (t of tabs; track t.id) { <button [class.on]="tab === t.id" (click)="setTab(t.id)">{{ t.label }}</button> }
    </div>

    @if (tab === 'reviews') {
      <div class="card row">
        <div class="grow"><label>Review cycle</label>
          <select [(ngModel)]="cycleId" (ngModelChange)="loadReviews()">
            <option [ngValue]="null">All cycles</option>
            @for (c of cycles; track c.id) { <option [ngValue]="c.id">{{ c.name }}{{ c.isOpen ? '' : ' (closed)' }}</option> }
          </select></div>
      </div>
      <div class="card scroll">
        <table>
          <thead><tr><th>Rank</th><th>Employee</th><th>Cycle</th><th class="num">KPI</th><th class="num">Attendance</th><th class="num">Overall</th><th>Rating</th><th>Status</th><th></th></tr></thead>
          <tbody>
            @for (r of reviews; track r.id; let i = $index) {
              <tr>
                <td>{{ i + 1 }}</td>
                <td><b>{{ r.employeeName }}</b><div class="muted small">{{ r.employeeCode }} {{ r.department ? '· ' + r.department : '' }}</div></td>
                <td>{{ r.cycle }}</td>
                <td class="num">{{ r.kpiScore | number: '1.0-1' }}</td>
                <td class="num">{{ r.attendanceScore | number: '1.0-1' }}</td>
                <td class="num"><b>{{ r.overallScore | number: '1.0-1' }}</b></td>
                <td>{{ r.rating }}</td>
                <td><span class="badge {{ badge(r.status) }}">{{ r.status }}</span></td>
                <td>@if (r.status === 'Draft') { <button class="sm" (click)="submitReview(r)">Submit</button> }</td>
              </tr>
            } @empty { <tr><td colspan="9" class="empty">No reviews yet.</td></tr> }
          </tbody>
        </table>
      </div>
    }

    @if (tab === 'new') {
      <div class="card">
        <h2>Evaluate an employee</h2>
        <div class="grid">
          <div><label>Employee</label>
            <select [(ngModel)]="rv.employeeId">
              @for (e of employees; track e.id) { <option [ngValue]="e.id">{{ e.employeeCode }} - {{ e.fullName }}</option> }
            </select></div>
          <div><label>Review cycle</label>
            <select [(ngModel)]="rv.reviewCycleId">
              @for (c of openCycles(); track c.id) { <option [ngValue]="c.id">{{ c.name }}</option> }
            </select></div>
          <div class="span2"><label>Reviewer</label><input [(ngModel)]="rv.reviewerName" /></div>
        </div>
        <table style="margin-top: 14px">
          <thead><tr><th>KPI</th><th style="width: 110px">Weight %</th><th style="width: 160px">Score (1-5)</th></tr></thead>
          <tbody>
            @for (i of rv.items; track i.kpiName) {
              <tr>
                <td>{{ i.kpiName }}</td>
                <td><input type="number" min="1" [(ngModel)]="i.weight" /></td>
                <td><select [(ngModel)]="i.score">@for (s of scores; track s) { <option [ngValue]="s">{{ s }}{{ label(s) }}</option> }</select></td>
              </tr>
            }
          </tbody>
        </table>
        <p>KPI score preview: <b>{{ kpiPreview() | number: '1.0-1' }}%</b> <span class="muted small">(attendance is added when you save, from processed attendance in the cycle)</span></p>
        <div><label>Comments</label><textarea [(ngModel)]="rv.comments"></textarea></div>
        <div class="row" style="margin-top: 10px"><button (click)="saveReview()">Save draft review</button></div>
        @if (saved) {
          <div class="panel">
            <b>{{ saved.rating }}</b> &mdash; overall {{ saved.overallScore | number: '1.0-1' }}
            (KPI {{ saved.kpiScore | number: '1.0-1' }}, attendance {{ saved.attendanceScore | number: '1.0-1' }}).
            @if (!saved.attendanceDataAvailable) { <div class="small">No processed attendance in this cycle, so the score is KPI only.</div> }
          </div>
        }
      </div>
    }

    @if (tab === 'setup') {
      <div class="row" style="align-items: flex-start">
        <div class="card grow">
          <h2>Review cycles</h2>
          @for (c of cycles; track c.id) {
            <div class="row spread" style="padding: 5px 0; border-bottom: 1px solid var(--line)">
              <span>{{ c.name }} <span class="muted small">{{ c.startDate | date: 'd MMM yyyy' }} - {{ c.endDate | date: 'd MMM yyyy' }}</span></span>
              @if (c.isOpen) { <button class="sm ghost" (click)="closeCycle(c)">Close</button> } @else { <span class="badge b-gray">Closed</span> }
            </div>
          }
          <div class="grid" style="margin-top: 12px">
            <div class="span2"><label>Name</label><input [(ngModel)]="nc.name" /></div>
            <div><label>Start</label><input type="date" [(ngModel)]="nc.startDate" /></div>
            <div><label>End</label><input type="date" [(ngModel)]="nc.endDate" /></div>
          </div>
          <button style="margin-top: 10px" (click)="addCycle()">Add cycle</button>
        </div>

        <div class="card grow">
          <h2>KPI templates <span class="muted small">(total weight {{ totalWeight() }}%)</span></h2>
          @for (k of kpis; track k.id) {
            <div class="row spread" style="padding: 5px 0; border-bottom: 1px solid var(--line)">
              <span>{{ k.name }} <span class="muted small">{{ k.weight }}%</span></span>
              <button class="sm ghost" (click)="deleteKpi(k)">Remove</button>
            </div>
          }
          <div class="grid" style="margin-top: 12px">
            <div class="span2"><label>KPI name</label><input [(ngModel)]="nk.name" /></div>
            <div><label>Weight %</label><input type="number" min="1" [(ngModel)]="nk.weight" /></div>
          </div>
          <button style="margin-top: 10px" (click)="addKpi()">Add KPI</button>
        </div>
      </div>
    }
  `
})
export class PerformanceComponent implements OnInit {
  private api = inject(Api);
  private n = inject(Notify);
  badge = badge;
  scores = [1, 2, 3, 4, 5];
  tabs = [{ id: 'reviews', label: 'Reviews & ranking' }, { id: 'new', label: 'New review' }, { id: 'setup', label: 'Cycles & KPIs' }];
  tab = 'reviews';

  employees: any[] = [];
  cycles: any[] = [];
  kpis: any[] = [];
  reviews: any[] = [];
  cycleId: number | null = null;

  rv: any = { employeeId: null, reviewCycleId: null, reviewerName: '', comments: '', items: [] };
  saved: any = null;
  nc: any = { name: '', startDate: today(), endDate: today() };
  nk: any = { name: '', weight: 10 };

  async ngOnInit() {
    this.employees = (await this.api.get<any[]>('employees', { activeOnly: true })) ?? [];
    await this.loadSetup();
    this.cycleId = this.cycles[0]?.id ?? null;
    await this.loadReviews();
  }

  async loadSetup() {
    this.cycles = (await this.api.get<any[]>('performance/cycles')) ?? [];
    this.kpis = (await this.api.get<any[]>('performance/kpis')) ?? [];
  }

  async setTab(t: string) {
    this.tab = t;
    if (t === 'reviews') await this.loadReviews();
    if (t === 'new') this.startReview();
    if (t === 'setup') await this.loadSetup();
  }

  openCycles() { return this.cycles.filter(c => c.isOpen); }
  label(s: number) { return ['', ' - Poor', ' - Below', ' - Meets', ' - Good', ' - Excellent'][s]; }

  async loadReviews() {
    this.reviews = (await this.api.get<any[]>('performance/reviews', { cycleId: this.cycleId })) ?? [];
  }

  startReview() {
    this.saved = null;
    this.rv = {
      employeeId: this.employees[0]?.id ?? null,
      reviewCycleId: this.openCycles()[0]?.id ?? null,
      reviewerName: '', comments: '',
      items: this.kpis.map(k => ({ kpiName: k.name, weight: k.weight, score: 3 }))
    };
  }

  kpiPreview() {
    const items: any[] = this.rv.items;
    const w = items.reduce((s, i) => s + (+i.weight || 0), 0);
    if (!w) return 0;
    return items.reduce((s, i) => s + ((+i.weight || 0) * (+i.score || 0)) / 5, 0) / w * 100;
  }

  async saveReview() {
    if (!this.rv.employeeId || !this.rv.reviewCycleId) { this.n.error('Pick an employee and an open review cycle.'); return; }
    const res = await this.api.post('performance/reviews', {
      employeeId: this.rv.employeeId, reviewCycleId: this.rv.reviewCycleId,
      reviewerName: nz(this.rv.reviewerName), comments: nz(this.rv.comments),
      items: this.rv.items.map((i: any) => ({ kpiName: i.kpiName, weight: +i.weight, score: +i.score }))
    });
    if (res) { this.saved = res; this.n.ok('Draft review saved. Submit it from the Reviews tab.'); }
  }

  async submitReview(r: any) {
    if (!confirm('Submit this review? It cannot be changed afterwards.')) return;
    if (await this.api.post(`performance/reviews/${r.id}/submit`)) { this.n.ok('Review submitted.'); await this.loadReviews(); }
  }

  async addCycle() {
    if (await this.api.post('performance/cycles', this.nc)) { this.n.ok('Cycle added.'); this.nc.name = ''; await this.loadSetup(); }
  }

  async closeCycle(c: any) {
    if (!confirm('Close this cycle? Reviews in it can no longer be created or edited.')) return;
    if (await this.api.post(`performance/cycles/${c.id}/close`)) await this.loadSetup();
  }

  totalWeight() { return this.kpis.reduce((s, k) => s + k.weight, 0); }

  async addKpi() {
    if (await this.api.post('performance/kpis', { name: this.nk.name, weight: this.nk.weight })) {
      this.n.ok('KPI added.');
      this.nk.name = '';
      await this.loadSetup();
    }
  }

  async deleteKpi(k: any) {
    if (await this.api.delete(`performance/kpis/${k.id}`)) await this.loadSetup();
  }
}

import { Component, OnInit, inject } from '@angular/core';
import { DatePipe, DecimalPipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute } from '@angular/router';
import { Api, Notify } from '../core/api.service';
import { badge, nz, today } from '../core/util';

type PanelType = 'interview' | 'result' | 'approve' | 'reject' | 'select' | 'decline' | 'appoint' | 'join';

@Component({
  selector: 'app-pipeline',
  standalone: true,
  imports: [FormsModule, DatePipe, DecimalPipe],
  template: `
    <h1>Selection pipeline</h1>
    <p class="sub">Shortlist, interview, merit list, approval, final selection, appointment letter, joining letter, add as employee.</p>

    <div class="card row">
      <div class="grow"><label>Circular</label>
        <select [(ngModel)]="circularId" (ngModelChange)="load()">
          @for (c of circulars; track c.id) { <option [ngValue]="c.id">{{ c.title }} ({{ c.status }})</option> }
        </select></div>
      <button class="ghost" [disabled]="!circularId" (click)="meritList()">Build merit list</button>
      <button class="ghost" [disabled]="!circularId" (click)="shortlistAll()">Shortlist all new CVs</button>
    </div>

    <div class="chips">
      @for (s of stages; track s) {
        <button class="chip" [class.on]="filter === s" (click)="filter = s">{{ s }} ({{ count(s) }})</button>
      }
    </div>

    @if (panel) {
      <div class="panel">
        <h2>{{ title() }} &mdash; {{ panel.app.fullName }}</h2>
        @switch (panel.type) {
          @case ('interview') {
            <div class="grid">
              <div><label>Date and time</label><input type="datetime-local" [(ngModel)]="f.scheduledAt" /></div>
              <div><label>Round</label><input type="number" min="1" [(ngModel)]="f.round" /></div>
              <div><label>Panel</label><input [(ngModel)]="f.panel" /></div>
              <div><label>Venue / link</label><input [(ngModel)]="f.venue" /></div>
            </div>
          }
          @case ('result') {
            <div class="grid">
              <div><label>Interview</label>
                <select [(ngModel)]="f.interviewId">
                  @for (i of pendingInterviews(); track i.id) { <option [ngValue]="i.id">Round {{ i.round }} - {{ i.scheduledAt | date: 'd MMM, h:mm a' }}</option> }
                </select></div>
              <div><label>Score (0-100)</label><input type="number" min="0" max="100" [(ngModel)]="f.score" /></div>
              <div class="span2"><label>Feedback</label><input [(ngModel)]="f.feedback" /></div>
            </div>
          }
          @case ('appoint') {
            <div class="grid">
              <div><label>Offered gross salary (monthly)</label><input type="number" min="0" [(ngModel)]="f.salary" /></div>
              <div><label>Joining date</label><input type="date" [(ngModel)]="f.joiningDate" /></div>
            </div>
          }
          @case ('join') {
            <div class="grid">
              <div><label>Actual joining date</label><input type="date" [(ngModel)]="f.actualJoiningDate" /></div>
              <div class="span2"><label>Remarks</label><input [(ngModel)]="f.remarks" /></div>
            </div>
          }
          @default {
            <div><label>Remarks</label><input [(ngModel)]="f.remarks" /></div>
          }
        }
        <div class="row" style="margin-top: 10px">
          <button (click)="submit()">Confirm</button>
          <button class="ghost" (click)="panel = null">Cancel</button>
        </div>
      </div>
    }

    <div class="card scroll">
      <table>
        <thead><tr><th>#</th><th>Candidate</th><th>Expected</th><th>Interviews</th><th class="num">Score</th><th>Stage</th><th>Next step</th></tr></thead>
        <tbody>
          @for (a of shown; track a.id) {
            <tr>
              <td>{{ a.meritRank ?? '' }}</td>
              <td><b>{{ a.fullName }}</b>
                <div class="muted small">{{ a.email }} {{ a.phone ? '· ' + a.phone : '' }}</div>
                @if (a.hasCv) { <a class="small" [href]="'/api/recruitment/applications/' + a.id + '/cv'" target="_blank">{{ a.cvFileName }}</a> }
              </td>
              <td class="num">{{ a.expectedSalary | number: '1.0-0' }}</td>
              <td>
                @for (i of a.interviews; track i.id) {
                  <div class="small">R{{ i.round }}: {{ i.isCompleted ? (i.score | number: '1.0-1') : 'scheduled ' + (i.scheduledAt | date: 'd MMM h:mm a') }}</div>
                }
              </td>
              <td class="num">{{ a.totalScore !== null ? (a.totalScore | number: '1.0-2') : '' }}</td>
              <td><span class="badge {{ badge(a.status) }}">{{ a.status }}</span>
                @if (a.approvalRemarks || a.decisionRemarks) { <div class="muted small">{{ a.decisionRemarks || a.approvalRemarks }}</div> }</td>
              <td>
                <div class="actions">
                  @switch (a.status) {
                    @case ('Applied') { <button class="sm" (click)="shortlist(a)">Shortlist</button> }
                    @case ('Shortlisted') {
                      <button class="sm" (click)="open('interview', a)">Schedule interview</button>
                      @if (hasPending(a)) { <button class="sm ghost" (click)="open('result', a)">Record result</button> }
                    }
                    @case ('Interviewed') {
                      <button class="sm ghost" (click)="open('interview', a)">Another round</button>
                      @if (hasPending(a)) { <button class="sm ghost" (click)="open('result', a)">Record result</button> }
                    }
                    @case ('MeritListed') {
                      <button class="sm" (click)="open('approve', a)">Approve</button>
                      <button class="sm danger" (click)="open('reject', a)">Reject</button>
                    }
                    @case ('Approved') {
                      <button class="sm" (click)="open('select', a)">Select</button>
                      <button class="sm danger" (click)="open('decline', a)">Reject</button>
                    }
                    @case ('Selected') { <button class="sm" (click)="open('appoint', a)">Appointment letter</button> }
                    @case ('Appointed') {
                      <button class="sm ghost" (click)="viewLetter(a)">View letter</button>
                      <button class="sm" (click)="open('join', a)">Joining letter</button>
                    }
                    @case ('JoiningSubmitted') { <button class="sm" (click)="addEmployee(a)">Add as employee</button> }
                  }
                </div>
              </td>
            </tr>
          } @empty { <tr><td colspan="7" class="empty">No candidates in this view.</td></tr> }
        </tbody>
      </table>
    </div>

    @if (letter) {
      <div class="card">
        <div class="row spread"><h2>Appointment letter {{ letter.referenceNo }}</h2>
          <div class="actions"><button class="sm ghost" (click)="print()">Print</button><button class="sm ghost" (click)="letter = null">Close</button></div></div>
        <pre style="white-space: pre-wrap; font: inherit; margin: 0">{{ letter.body }}</pre>
      </div>
    }
  `
})
export class PipelineComponent implements OnInit {
  private api = inject(Api);
  private n = inject(Notify);
  private route = inject(ActivatedRoute);
  badge = badge;

  stages = ['All', 'Applied', 'Shortlisted', 'Interviewed', 'MeritListed', 'Approved', 'Selected', 'Appointed',
    'JoiningSubmitted', 'Joined', 'Rejected'];
  circulars: any[] = [];
  circularId: number | null = null;
  apps: any[] = [];
  filter = 'All';
  panel: { type: PanelType; app: any } | null = null;
  f: any = {};
  letter: any = null;

  async ngOnInit() {
    const all = (await this.api.get<any[]>('recruitment/circulars')) ?? [];
    this.circulars = all.filter(c => c.status !== 'Draft');
    const q = this.route.snapshot.queryParamMap.get('circularId');
    this.circularId = q ? +q : this.circulars[0]?.id ?? null;
    await this.load();
  }

  async load() {
    this.panel = null;
    this.letter = null;
    if (!this.circularId) { this.apps = []; return; }
    this.apps = (await this.api.get<any[]>(`recruitment/circulars/${this.circularId}/applications`)) ?? [];
  }

  get shown() { return this.filter === 'All' ? this.apps : this.apps.filter(a => a.status === this.filter); }
  count(s: string) { return s === 'All' ? this.apps.length : this.apps.filter(a => a.status === s).length; }
  hasPending(a: any) { return a.interviews.some((i: any) => !i.isCompleted); }
  pendingInterviews() { return this.panel?.app.interviews.filter((i: any) => !i.isCompleted) ?? []; }

  title() {
    const t: Record<string, string> = {
      interview: 'Schedule interview', result: 'Record interview result', approve: 'Approve candidate',
      reject: 'Reject candidate', select: 'Final selection', decline: 'Reject candidate',
      appoint: 'Issue appointment letter', join: 'Submit joining letter'
    };
    return t[this.panel!.type];
  }

  open(type: PanelType, app: any) {
    this.panel = { type, app };
    this.f = { remarks: '' };
    if (type === 'interview') this.f = { scheduledAt: '', round: app.interviews.length + 1, panel: '', venue: '' };
    if (type === 'result') this.f = { interviewId: app.interviews.find((i: any) => !i.isCompleted)?.id, score: null, feedback: '' };
    if (type === 'appoint') this.f = { salary: app.expectedSalary, joiningDate: today() };
    if (type === 'join') this.f = { actualJoiningDate: today(), remarks: '' };
  }

  async submit() {
    if (!this.panel) return;
    const { type, app } = this.panel;
    let res: any = null;
    switch (type) {
      case 'interview':
        if (!this.f.scheduledAt) { this.n.error('Pick the interview date and time.'); return; }
        res = await this.api.post(`recruitment/applications/${app.id}/interviews`,
          { scheduledAt: this.f.scheduledAt, round: this.f.round, panel: nz(this.f.panel), venue: nz(this.f.venue) });
        break;
      case 'result':
        if (this.f.score === null || this.f.score === '') { this.n.error('Enter a score.'); return; }
        res = await this.api.post(`recruitment/interviews/${this.f.interviewId}/result`,
          { score: this.f.score, feedback: nz(this.f.feedback) });
        break;
      case 'approve': case 'reject':
        res = await this.api.post(`recruitment/applications/${app.id}/approval`,
          { approve: type === 'approve', remarks: nz(this.f.remarks) });
        break;
      case 'select': case 'decline':
        res = await this.api.post(`recruitment/applications/${app.id}/decision`,
          { selected: type === 'select', remarks: nz(this.f.remarks) });
        break;
      case 'appoint':
        res = await this.api.post(`recruitment/applications/${app.id}/appointment`,
          { offeredGrossSalary: this.f.salary, joiningDate: this.f.joiningDate });
        if (res) this.letter = res;
        break;
      case 'join':
        res = await this.api.post(`recruitment/applications/${app.id}/joining`,
          { actualJoiningDate: this.f.actualJoiningDate, remarks: nz(this.f.remarks) });
        break;
    }
    if (!res) return;
    this.n.ok('Saved.');
    const keepLetter = this.letter;
    await this.load();
    this.letter = keepLetter;
  }

  async shortlist(a: any) {
    if (await this.api.post('recruitment/applications/shortlist', { ids: [a.id] })) { this.n.ok('Shortlisted.'); await this.load(); }
  }

  async shortlistAll() {
    const ids = this.apps.filter(a => a.status === 'Applied').map(a => a.id);
    if (!ids.length) { this.n.error('No new CVs to shortlist.'); return; }
    if (!confirm(`Shortlist ${ids.length} candidate(s)?`)) return;
    if (await this.api.post('recruitment/applications/shortlist', { ids })) { this.n.ok('Shortlisted.'); await this.load(); }
  }

  async meritList() {
    if (await this.api.post(`recruitment/circulars/${this.circularId}/merit-list`)) {
      this.n.ok('Merit list built from interview scores.');
      await this.load();
    }
  }

  async addEmployee(a: any) {
    const r: any = await this.api.post(`recruitment/applications/${a.id}/add-employee`);
    if (r) { this.n.ok(`Added as ${r.employeeCode}. Review their salary on the Employees page.`); await this.load(); }
  }

  async viewLetter(a: any) {
    this.letter = await this.api.get(`recruitment/applications/${a.id}/appointment`);
  }

  print() { window.print(); }
}

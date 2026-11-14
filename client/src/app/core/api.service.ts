import { Injectable, inject, signal } from '@angular/core';
import { HttpClient, HttpErrorResponse, HttpParams } from '@angular/common/http';
import { firstValueFrom } from 'rxjs';

export interface Toast { kind: 'ok' | 'error'; text: string; }

/** Small toast notifier shown by AppComponent. */
@Injectable({ providedIn: 'root' })
export class Notify {
  readonly toast = signal<Toast | null>(null);
  private timer: any;

  ok(text: string) { this.show({ kind: 'ok', text }); }
  error(text: string) { this.show({ kind: 'error', text }); }

  private show(t: Toast) {
    this.toast.set(t);
    clearTimeout(this.timer);
    this.timer = setTimeout(() => this.toast.set(null), t.kind === 'error' ? 7000 : 3000);
  }
}

/**
 * Thin wrapper over HttpClient. Every call resolves to the response body, or to null after showing an
 * error toast, so components can simply check `if (!result) return;`.
 */
@Injectable({ providedIn: 'root' })
export class Api {
  private http = inject(HttpClient);
  private notify = inject(Notify);
  private base = '/api/';

  get<T>(url: string, params?: Record<string, any>): Promise<T | null> {
    let p = new HttpParams();
    for (const k of Object.keys(params ?? {})) {
      const v = params![k];
      if (v !== null && v !== undefined && v !== '') p = p.set(k, String(v));
    }
    return this.run(this.http.get<T>(this.base + url, { params: p }));
  }

  post<T>(url: string, body: any = {}): Promise<T | null> {
    return this.run(this.http.post<T>(this.base + url, body));
  }

  postForm<T>(url: string, form: FormData): Promise<T | null> {
    return this.run(this.http.post<T>(this.base + url, form));
  }

  put<T>(url: string, body: any = {}): Promise<T | null> {
    return this.run(this.http.put<T>(this.base + url, body));
  }

  delete(url: string): Promise<boolean> {
    return this.run(this.http.delete(this.base + url, { observe: 'response' })).then(r => r !== null);
  }

  private async run<T>(obs: any): Promise<T | null> {
    try {
      const res = await firstValueFrom(obs);
      return (res ?? ({} as any)) as T;
    } catch (e) {
      this.notify.error(this.message(e));
      return null;
    }
  }

  private message(e: unknown): string {
    if (e instanceof HttpErrorResponse) {
      if (e.status === 0) return 'Cannot reach the server. Is the API running?';
      if (e.status === 200) return 'The server replied with something that is not JSON. The backend is probably outdated: stop it and run "dotnet run" again.';
      const b: any = e.error;
      if (b?.message) return b.message;
      if (b?.errors) {
        const first = Object.values(b.errors as Record<string, string[]>)[0];
        if (first?.length) return first[0];
      }
      if (b?.title) return b.title;
      return `Request failed (${e.status})`;
    }
    return 'Something went wrong.';
  }
}

import { KeyValuePipe, LowerCasePipe } from '@angular/common';
import { MatButtonModule } from '@angular/material/button';
import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { Component, inject, signal } from '@angular/core';
import { HealthReport, HealthStatusValue, isHealthReport } from './health-report';

const CHECK_LABELS: Record<string, string> = {
  postgres: 'Veritabanı',
  'ai-service': 'Yapay zekâ servisi',
};

const STATUS_LABELS: Record<HealthStatusValue, string> = {
  Healthy: 'Çalışıyor',
  Degraded: 'Yavaş',
  Unhealthy: 'Çalışmıyor',
};

@Component({
  selector: 'app-health-status',
  imports: [KeyValuePipe, LowerCasePipe, MatButtonModule],
  templateUrl: './health-status.html',
  styleUrl: './health-status.scss',
})
export class HealthStatus {
  private readonly http = inject(HttpClient);

  protected readonly report = signal<HealthReport | null>(null);
  protected readonly apiUnreachable = signal(false);
  protected readonly loading = signal(false);

  protected readonly checkLabel = (key: string) => CHECK_LABELS[key] ?? key;
  protected readonly statusLabel = (status: HealthStatusValue) => STATUS_LABELS[status];

  constructor() {
    this.refresh();
  }

  protected refresh(): void {
    this.loading.set(true);
    this.http.get<HealthReport>('/health').subscribe({
      next: (report) => this.show(report),
      // The API answers 503 with the same body when a dependency is down.
      error: (error: HttpErrorResponse) =>
        this.show(isHealthReport(error.error) ? error.error : null),
    });
  }

  private show(report: HealthReport | null): void {
    this.report.set(report);
    this.apiUnreachable.set(report === null);
    this.loading.set(false);
  }
}

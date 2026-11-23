import { Component, computed, inject, signal } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatButtonToggleModule } from '@angular/material/button-toggle';
import { MatDialog } from '@angular/material/dialog';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { firstValueFrom } from 'rxjs';
import {
  AnalyticsApi,
  AnalyticsList,
  AnalyticsPeriod,
  AnalyticsSummary,
  AnsweredQuestion,
  TopDocument,
  TopQuestion,
} from '../api/analytics';
import { ConversationDialog, ConversationDialogData } from './conversation-dialog';

interface Report {
  summary: AnalyticsSummary;
  unanswered: AnalyticsList<AnsweredQuestion>;
  negative: AnalyticsList<AnsweredQuestion>;
  documents: AnalyticsList<TopDocument>;
  questions: AnalyticsList<TopQuestion>;
}

// Rows shown in each list; the total is shown next to the heading.
const listLimit = 20;

// The API counts days in the school's time zone (Analytics:TimeZone), so "today" is taken
// there too rather than in the browser's.
const schoolTimeZone = 'Europe/Istanbul';

const dateTimeFormat = new Intl.DateTimeFormat('tr-TR', { dateStyle: 'medium', timeStyle: 'short' });
const dayFormat = new Intl.DateTimeFormat('tr-TR', { day: 'numeric', month: 'short', timeZone: 'UTC' });
const percentFormat = new Intl.NumberFormat('tr-TR', { style: 'percent', maximumFractionDigits: 0 });

// What students ask and how it goes (PLANNING D5). Staff look here for what the documents
// lack: unanswered questions and 👎 answers come first.
@Component({
  imports: [MatButtonModule, MatButtonToggleModule, MatProgressSpinnerModule],
  selector: 'app-analytics-page',
  styleUrl: './analytics-page.scss',
  templateUrl: './analytics-page.html',
})
export class AnalyticsPage {
  private readonly api = inject(AnalyticsApi);
  private readonly dialog = inject(MatDialog);

  protected readonly periods = [7, 30, 90];
  protected readonly days = signal(30);
  protected readonly report = signal<Report | null>(null);
  protected readonly loading = signal(true);
  protected readonly loadFailed = signal(false);

  // The tallest day sets the scale of the chart.
  protected readonly busiestDay = computed(() =>
    Math.max(1, ...(this.report()?.summary.days.map((d) => d.questions) ?? [])),
  );

  // Answers that got any feedback; 👍 share is counted among them.
  protected readonly rated = computed(() => {
    const summary = this.report()?.summary;
    return summary ? summary.up + summary.down : 0;
  });

  // Ignores answers to stale periods when the period is changed quickly.
  private requestId = 0;

  constructor() {
    void this.load();
  }

  protected selectPeriod(days: number): void {
    if (days !== this.days()) {
      this.days.set(days);
      void this.load();
    }
  }

  protected async load(): Promise<void> {
    const id = ++this.requestId;
    const period = lastDays(this.days());
    this.loading.set(true);
    this.loadFailed.set(false);

    try {
      const [summary, unanswered, negative, documents, questions] = await Promise.all([
        firstValueFrom(this.api.summary(period)),
        firstValueFrom(this.api.unanswered(period, listLimit)),
        firstValueFrom(this.api.negativeFeedback(period, listLimit)),
        firstValueFrom(this.api.topDocuments(period, listLimit)),
        firstValueFrom(this.api.topQuestions(period, listLimit)),
      ]);
      if (id === this.requestId) {
        this.report.set({ summary, unanswered, negative, documents, questions });
      }
    } catch {
      if (id === this.requestId) {
        this.loadFailed.set(true);
      }
    } finally {
      if (id === this.requestId) {
        this.loading.set(false);
      }
    }
  }

  protected openConversation(conversationId: string, messageId: string | null = null): void {
    this.dialog.open<ConversationDialog, ConversationDialogData>(ConversationDialog, {
      data: { conversationId, messageId },
      width: '48rem',
      maxWidth: 'calc(100vw - 2rem)',
      autoFocus: 'dialog',
    });
  }

  protected share(part: number, whole: number): string {
    return whole === 0 ? '–' : percentFormat.format(part / whole);
  }

  protected barHeight(count: number): string {
    return `${(count / this.busiestDay()) * 100}%`;
  }

  protected dayLabel(date: string): string {
    return dayFormat.format(new Date(`${date}T00:00:00Z`));
  }

  protected formatDateTime(value: string): string {
    return dateTimeFormat.format(new Date(value));
  }

  protected periodLabel(period: AnalyticsPeriod): string {
    return `${this.dayLabel(period.from)} – ${this.dayLabel(period.to)}`;
  }
}

// The last n days including today, in the school's time zone.
function lastDays(days: number): AnalyticsPeriod {
  // en-CA writes dates as yyyy-MM-dd.
  const today = new Intl.DateTimeFormat('en-CA', { timeZone: schoolTimeZone }).format(new Date());
  const from = new Date(`${today}T00:00:00Z`);
  from.setUTCDate(from.getUTCDate() - (days - 1));
  return { from: from.toISOString().slice(0, 10), to: today };
}

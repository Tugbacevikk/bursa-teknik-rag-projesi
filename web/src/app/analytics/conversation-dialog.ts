import { HttpErrorResponse } from '@angular/common/http';
import { Component, inject, signal } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MAT_DIALOG_DATA, MatDialogModule } from '@angular/material/dialog';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { firstValueFrom } from 'rxjs';
import { AnalyticsApi, StaffConversation, StaffMessage } from '../api/analytics';

export interface ConversationDialogData {
  conversationId: string;
  // The answer the row was about, marked in the chat.
  messageId: string | null;
}

const dateTimeFormat = new Intl.DateTimeFormat('tr-TR', { dateStyle: 'medium', timeStyle: 'short' });

// A student's chat, read-only, as stored: the questions are masked (PLANNING C10) and the
// answers carry every source the model was given, cited or not.
@Component({
  imports: [MatButtonModule, MatDialogModule, MatProgressSpinnerModule],
  selector: 'app-conversation-dialog',
  template: `
    <h2 mat-dialog-title>Sohbet</h2>
    <mat-dialog-content>
      @if (conversation(); as c) {
        <p class="meta">Başlangıç: {{ format(c.createdAt) }} · {{ c.messages.length / 2 }} soru</p>
        <ol class="messages">
          @for (m of c.messages; track m.id) {
            <li class="message" [class.question]="m.role === 'User'" [class.marked]="m.id === data.messageId">
              <span class="who">{{ m.role === 'User' ? 'Öğrenci' : 'Asistan' }} · {{ format(m.createdAt) }}</span>
              <p class="text">{{ m.content }}</p>
              @if (m.role === 'Assistant') {
                <span class="details">
                  {{ m.outcome === 'NotFound' ? 'Bulunamadı' : 'Cevaplandı' }}
                  @if (m.model) {
                    · {{ m.model }}
                  }
                  @if (m.durationMs !== null) {
                    · {{ seconds(m) }} sn
                  }
                  @if (m.feedback) {
                    · {{ m.feedback === 'Up' ? '👍' : '👎' }}
                  }
                </span>
                @if (m.feedbackComment) {
                  <span class="comment">Yorum: “{{ m.feedbackComment }}”</span>
                }
                @if (m.sources.length > 0) {
                  <details class="sources">
                    <summary>Modele verilen kaynaklar ({{ m.sources.length }})</summary>
                    <ol>
                      @for (s of m.sources; track s.number) {
                        <li>
                          [{{ s.number }}] {{ s.title }}
                          @if (s.sectionPath) {
                            · {{ s.sectionPath }}
                          }
                          @if (s.pageNumber) {
                            · s. {{ s.pageNumber }}
                          }
                        </li>
                      }
                    </ol>
                  </details>
                }
              }
            </li>
          }
        </ol>
      } @else if (error(); as message) {
        <p role="alert">{{ message }}</p>
      } @else {
        <div class="center"><mat-spinner diameter="28" aria-label="Sohbet yükleniyor" /></div>
      }
    </mat-dialog-content>
    <mat-dialog-actions align="end">
      <button matButton type="button" mat-dialog-close>Kapat</button>
    </mat-dialog-actions>
  `,
  styles: `
    .meta,
    .who,
    .details {
      font: var(--mat-sys-body-small);
      color: var(--mat-sys-on-surface-variant);
    }

    .meta {
      margin-top: 0;
    }

    .messages {
      display: grid;
      gap: 0.75rem;
      margin: 0;
      padding: 0;
      list-style: none;
    }

    .message {
      display: grid;
      gap: 0.25rem;
      padding: 0.625rem 0.75rem;
      border-radius: 0.5rem;
      background: #f5f7fa;
    }

    .message.question {
      background: #e3ecf6;
    }

    .message.marked {
      outline: 2px solid #f59e0b;
    }

    .text {
      margin: 0;
      white-space: pre-wrap;
      overflow-wrap: anywhere;
    }

    .comment {
      color: #8a3b00;
      font-style: italic;
    }

    .sources {
      font: var(--mat-sys-body-small);
    }

    .sources ol {
      margin: 0.25rem 0 0;
      padding-inline-start: 1rem;
      list-style: none;
    }

    .center {
      display: grid;
      place-items: center;
      padding: 2rem;
    }
  `,
})
export class ConversationDialog {
  private readonly api = inject(AnalyticsApi);
  protected readonly data = inject<ConversationDialogData>(MAT_DIALOG_DATA);

  protected readonly conversation = signal<StaffConversation | null>(null);
  protected readonly error = signal<string | null>(null);

  constructor() {
    void this.load();
  }

  protected format(value: string): string {
    return dateTimeFormat.format(new Date(value));
  }

  protected seconds(message: StaffMessage): string {
    return ((message.durationMs ?? 0) / 1000).toLocaleString('tr-TR', { maximumFractionDigits: 1 });
  }

  private async load(): Promise<void> {
    try {
      this.conversation.set(await firstValueFrom(this.api.conversation(this.data.conversationId)));
    } catch (error) {
      this.error.set(
        error instanceof HttpErrorResponse && error.status === 404
          ? 'Bu sohbet artık yok (saklama süresi dolmuş olabilir).'
          : 'Sohbet alınamadı.',
      );
    }
  }
}

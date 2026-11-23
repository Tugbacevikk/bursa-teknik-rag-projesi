import { HttpErrorResponse } from '@angular/common/http';
import { Component, ElementRef, inject, input, linkedSignal, signal, viewChild } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { firstValueFrom } from 'rxjs';
import { ChatApi, Rating, maxCommentLength } from '../api/chat';

// 👍/👎 under an answer (PLANNING D2). A 👎 is saved at once; the comment box that opens
// after it is optional.
@Component({
  imports: [MatButtonModule],
  selector: 'app-answer-feedback',
  styleUrl: './answer-feedback.scss',
  templateUrl: './answer-feedback.html',
})
export class AnswerFeedback {
  private readonly api = inject(ChatApi);

  readonly conversationId = input.required<string>();
  readonly messageId = input.required<string>();
  // What was given before, from the stored chat.
  readonly initialRating = input<Rating | null>(null);
  readonly initialComment = input<string | null>(null);

  protected readonly maxLength = maxCommentLength;
  protected readonly rating = linkedSignal(() => this.initialRating());
  protected readonly savedComment = linkedSignal(() => this.initialComment());
  protected readonly commenting = signal(false);
  protected readonly comment = signal('');
  protected readonly sending = signal(false);
  protected readonly error = signal<string | null>(null);

  private readonly commentInput = viewChild<ElementRef<HTMLTextAreaElement>>('commentInput');

  protected async rate(rating: Rating): Promise<void> {
    // Giving the same again would only clear a comment already sent.
    if (this.sending() || this.rating() === rating) {
      return;
    }

    if (await this.send(rating, null)) {
      this.rating.set(rating);
      this.savedComment.set(null);
      this.commenting.set(rating === 'Down');
      if (rating === 'Down') {
        this.comment.set('');
        this.showCommentBox();
      }
    }
  }

  protected async sendComment(): Promise<void> {
    const comment = this.comment().trim();
    if (this.sending() || comment === '' || comment.length > maxCommentLength) {
      return;
    }

    if (await this.send('Down', comment)) {
      this.savedComment.set(comment);
      this.commenting.set(false);
    }
  }

  protected openComment(): void {
    this.commenting.set(true);
    this.showCommentBox();
  }

  protected cancelComment(): void {
    this.commenting.set(false);
  }

  // Once drawn: the box opens at the bottom of the answer, often under the sticky question
  // box (its scroll-margin leaves room for it).
  private showCommentBox(): void {
    setTimeout(() => {
      const input = this.commentInput()?.nativeElement;
      input?.closest('form')?.scrollIntoView({ behavior: 'smooth', block: 'nearest' });
      input?.focus({ preventScroll: true });
    });
  }

  private async send(rating: Rating, comment: string | null): Promise<boolean> {
    this.sending.set(true);
    this.error.set(null);
    try {
      await firstValueFrom(this.api.setFeedback(this.conversationId(), this.messageId(), rating, comment));
      return true;
    } catch (error) {
      this.error.set(
        error instanceof HttpErrorResponse && error.status === 429
          ? 'Kısa sürede çok geri bildirim gönderdiniz, biraz sonra yeniden deneyin.'
          : 'Geri bildirim gönderilemedi, lütfen yeniden deneyin.',
      );
      return false;
    } finally {
      this.sending.set(false);
    }
  }
}

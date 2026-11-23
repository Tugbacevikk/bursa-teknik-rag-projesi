import { HttpErrorResponse } from '@angular/common/http';
import { Component, DestroyRef, ElementRef, OnInit, inject, signal, viewChild } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { ChatApi, ChatRequestError, ChatSource, Conversation, Rating, maxQuestionLength } from '../api/chat';
import { Session } from '../auth/session';
import { AnswerFeedback } from './answer-feedback';
import { Block, citedNumbers, formatAnswer } from './answer-format';

// One question and what came back for it.
interface Turn {
  key: number;
  question: string;
  // Grows while the answer streams in.
  answer: string;
  // Everything the answer was written from; only the cited ones are shown.
  sources: ChatSource[];
  status: 'streaming' | 'answered' | 'notFound' | 'failed';
  // The fixed text for notFound and failed.
  message: string | null;
  // Set once the answer is stored (answered, notFound): what 👍/👎 is given to.
  stored: { conversationId: string; messageId: string; feedback: Rating | null; feedbackComment: string | null } | null;
}

// Where the chat's id is kept (PLANNING C3): sessionStorage, so it is forgotten when the
// tab is closed and nothing stays on a shared computer in the library.
const conversationKey = 'btu-asistan.conversation';

// The students' chat (PLANNING C13): anonymous, at the site's root address. Also the
// widget the school's site shows in an iframe (/widget, D4): the same chat, compact and
// without the staff links.
@Component({
  imports: [AnswerFeedback, MatButtonModule, MatProgressSpinnerModule, RouterLink],
  host: { '[class.widget]': 'widget' },
  selector: 'app-chat-page',
  styleUrl: './chat-page.scss',
  templateUrl: './chat-page.html',
})
export class ChatPage implements OnInit {
  private readonly api = inject(ChatApi);
  protected readonly session = inject(Session);

  // Set by the /widget route (app.routes.ts).
  protected readonly widget = inject(ActivatedRoute).snapshot.data['widget'] === true;

  protected readonly maxLength = maxQuestionLength;
  protected readonly examples = [
    'İMEP başvuruları ne zaman yapılır?',
    'Ara faaliyet raporunu ne zaman teslim etmeliyim?',
    'İşletmede eğitim sırasında sigortamı kim öder?',
  ];

  protected readonly turns = signal<Turn[]>([]);
  protected readonly busy = signal(false);
  protected readonly loadingHistory = signal(false);
  // A signal like the rest: the app is zoneless, so a plain field cleared after sending
  // would stay on the screen.
  protected readonly question = signal('');

  // The source last jumped to from a citation, highlighted for a moment.
  protected readonly highlighted = signal<string | null>(null);

  private readonly input = viewChild<ElementRef<HTMLTextAreaElement>>('input');
  private conversationId: string | null = readStoredConversation();
  private nextKey = 0;
  private abort: AbortController | null = null;

  constructor() {
    inject(DestroyRef).onDestroy(() => this.abort?.abort());
  }

  async ngOnInit(): Promise<void> {
    if (this.conversationId) {
      await this.loadHistory(this.conversationId);
    }
  }

  protected async ask(text: string = this.question()): Promise<void> {
    const question = text.trim();
    if (this.busy() || question === '' || question.length > maxQuestionLength) {
      return;
    }

    this.busy.set(true);
    this.question.set('');
    const key = this.nextKey++;
    this.turns.update((turns) => [
      ...turns,
      { key, question, answer: '', sources: [], status: 'streaming', message: null, stored: null },
    ]);
    this.scrollToEnd();

    this.abort = new AbortController();
    let finished = false;
    try {
      for await (const event of this.api.ask(question, this.conversationId, this.abort.signal)) {
        switch (event.type) {
          case 'conversation':
            this.remember(event.conversationId);
            break;
          case 'sources':
            this.updateTurn(key, { sources: event.sources });
            break;
          case 'delta':
            this.updateTurn(key, (turn) => ({ answer: turn.answer + event.text }));
            this.scrollToEnd();
            break;
          case 'done':
            this.updateTurn(key, { status: 'answered', stored: this.stored(event.messageId) });
            finished = true;
            break;
          case 'not_found':
            this.updateTurn(key, {
              status: 'notFound',
              message: event.message,
              answer: '',
              sources: [],
              stored: this.stored(event.messageId),
            });
            finished = true;
            break;
          case 'error':
            this.fail(key, question, event.message);
            finished = true;
            break;
        }
      }

      if (!finished && !this.abort.signal.aborted) {
        this.fail(key, question, 'Bağlantı koptu. Lütfen sorunuzu yeniden gönderin.');
      }
    } catch (error) {
      if (!this.abort.signal.aborted) {
        this.fail(
          key,
          question,
          error instanceof ChatRequestError ? error.message : 'Şu an cevap veremiyorum, lütfen biraz sonra tekrar deneyin.',
        );
      }
    } finally {
      this.busy.set(false);
      this.abort = null;
      this.scrollToEnd();
      this.input()?.nativeElement.focus();
    }
  }

  // Enter sends; Shift+Enter starts a new line.
  protected onKeydown(event: KeyboardEvent): void {
    if (event.key === 'Enter' && !event.shiftKey && !event.isComposing) {
      event.preventDefault();
      void this.ask();
    }
  }

  protected newChat(): void {
    this.abort?.abort();
    this.turns.set([]);
    this.remember(null);
    this.question.set('');
    this.input()?.nativeElement.focus();
  }

  protected blocks(turn: Turn): Block[] {
    return formatAnswer(turn.answer, turn.sources.length);
  }

  // Only the sources the answer points at, in the order it first does.
  protected cited(turn: Turn): ChatSource[] {
    return citedNumbers(turn.answer, turn.sources.length).map((n) => turn.sources[n - 1]);
  }

  protected sourceId(turn: Turn, number: number): string {
    return `kaynak-${turn.key}-${number}`;
  }

  protected showSource(turn: Turn, number: number): void {
    const id = this.sourceId(turn, number);
    const element = document.getElementById(id);
    element?.scrollIntoView({ behavior: 'smooth', block: 'nearest' });
    element?.focus({ preventScroll: true });
    this.highlighted.set(id);
    setTimeout(() => {
      if (this.highlighted() === id) {
        this.highlighted.set(null);
      }
    }, 2000);
  }

  protected location(source: ChatSource): string {
    return [source.sectionPath, source.pageNumber ? `s. ${source.pageNumber}` : null].filter(Boolean).join(' · ');
  }

  private async loadHistory(conversationId: string): Promise<void> {
    this.loadingHistory.set(true);
    try {
      const conversation = await firstValueFrom(this.api.conversation(conversationId));
      this.turns.set(toTurns(conversation, () => this.nextKey++));
      this.scrollToEnd();
    } catch (error) {
      // Gone (deleted after the retention period) or unreadable: start afresh.
      if (!(error instanceof HttpErrorResponse) || error.status === 404) {
        this.remember(null);
      }
    } finally {
      this.loadingHistory.set(false);
    }
  }

  // A failed answer is dropped (nothing was stored) and the question is put back, so
  // asking again is one key press.
  private fail(key: number, question: string, message: string): void {
    this.updateTurn(key, { status: 'failed', message, answer: '', sources: [] });
    if (this.question() === '') {
      this.question.set(question);
    }
  }

  // The conversation event always comes first, so the id is known by then.
  private stored(messageId: string): Turn['stored'] {
    return this.conversationId ? { conversationId: this.conversationId, messageId, feedback: null, feedbackComment: null } : null;
  }

  private updateTurn(key: number, change: Partial<Turn> | ((turn: Turn) => Partial<Turn>)): void {
    this.turns.update((turns) =>
      turns.map((turn) =>
        turn.key === key ? { ...turn, ...(typeof change === 'function' ? change(turn) : change) } : turn,
      ),
    );
  }

  private remember(conversationId: string | null): void {
    this.conversationId = conversationId;
    try {
      if (conversationId) {
        sessionStorage.setItem(conversationKey, conversationId);
      } else {
        sessionStorage.removeItem(conversationKey);
      }
    } catch {
      // Storage blocked (e.g. some private modes): the chat still works for this page.
    }
  }

  // Follows the answer as it grows, unless the student has scrolled up to read.
  private scrollToEnd(): void {
    const nearEnd = window.innerHeight + window.scrollY >= document.documentElement.scrollHeight - 160;
    if (nearEnd) {
      requestAnimationFrame(() => window.scrollTo({ top: document.documentElement.scrollHeight }));
    }
  }
}

function readStoredConversation(): string | null {
  try {
    return sessionStorage.getItem(conversationKey);
  } catch {
    return null;
  }
}

// Stored messages come as question, answer, question, answer…; a question whose answer
// was never stored does not occur (both are saved together).
function toTurns({ conversationId, messages }: Conversation, key: () => number): Turn[] {
  const turns: Turn[] = [];
  for (let i = 0; i + 1 < messages.length; i += 2) {
    const [question, answer] = [messages[i], messages[i + 1]];
    const notFound = answer.outcome === 'NotFound';
    turns.push({
      key: key(),
      question: question.content,
      answer: notFound ? '' : answer.content,
      sources: answer.sources,
      status: notFound ? 'notFound' : 'answered',
      message: notFound ? answer.content : null,
      stored: { conversationId, messageId: answer.id, feedback: answer.feedback, feedbackComment: answer.feedbackComment },
    });
  }
  return turns;
}

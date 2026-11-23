import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { maxQuestionLength } from './search';

// The API's anonymous chat (backend: ChatEndpoints.cs, PLANNING C2).

export { maxQuestionLength };

export interface ChatSource {
  number: number;
  title: string;
  unit: string;
  sectionPath: string | null;
  pageNumber: number | null;
  sourceUrl: string | null;
  validityPeriod: string | null;
}

// One server-sent event of POST /api/chat, in the order they come.
export type ChatEvent =
  | { type: 'conversation'; conversationId: string }
  | { type: 'sources'; sources: ChatSource[] }
  | { type: 'delta'; text: string }
  | { type: 'done'; messageId: string }
  | { type: 'not_found'; messageId: string; message: string }
  | { type: 'error'; message: string };

// The request was refused before any answer started.
export class ChatRequestError extends Error {
  constructor(
    // 0: the API could not be reached.
    readonly status: number,
    message: string,
    // Seconds, for 429.
    readonly retryAfter: number | null = null,
  ) {
    super(message);
  }
}

export interface StoredMessage {
  id: string;
  role: 'User' | 'Assistant';
  content: string;
  outcome: 'Answered' | 'NotFound' | null;
  sources: ChatSource[];
  createdAt: string;
  feedback: Rating | null;
  feedbackComment: string | null;
}

// A student's 👍/👎 on an answer (PLANNING D2).
export type Rating = 'Up' | 'Down';

// The longest comment the API takes (MessageFeedbackRequest.MaxCommentLength).
export const maxCommentLength = 500;

export interface Conversation {
  conversationId: string;
  messages: StoredMessage[];
}

@Injectable({ providedIn: 'root' })
export class ChatApi {
  private readonly http = inject(HttpClient);

  /**
   * Asks a question and yields the answer's events as they arrive. Throws ChatRequestError
   * when the API refuses before answering (400, 429, 503, unreachable).
   *
   * fetch rather than HttpClient: HttpClient hands over a response only when it is
   * complete, while fetch exposes the body as a stream that can be read as it comes.
   * Interceptors do not see fetch, so the CSRF header (api-request.interceptor.ts) is
   * added here: staff signed in with the session cookie need it.
   */
  async *ask(question: string, conversationId: string | null, signal: AbortSignal): AsyncGenerator<ChatEvent> {
    let response: Response;
    try {
      response = await fetch('/api/chat', {
        method: 'POST',
        headers: {
          'Content-Type': 'application/json',
          Accept: 'text/event-stream',
          'X-Requested-With': 'XMLHttpRequest',
        },
        body: JSON.stringify({ question, conversationId }),
        signal,
      });
    } catch (error) {
      if (signal.aborted) {
        throw error;
      }
      throw new ChatRequestError(0, 'Sunucuya ulaşılamıyor. Bağlantınızı kontrol edip yeniden deneyin.');
    }

    if (!response.ok || !response.body) {
      throw await requestError(response);
    }

    yield* readServerSentEvents(response.body, signal);
  }

  // Replaces any earlier feedback on the answer; the comment is kept with Down only.
  setFeedback(conversationId: string, messageId: string, rating: Rating, comment: string | null = null): Observable<void> {
    return this.http.put<void>(
      `/api/chat/${encodeURIComponent(conversationId)}/messages/${encodeURIComponent(messageId)}/feedback`,
      { rating, comment },
    );
  }

  // The chat as stored, for a page reload. Fails with 404 when it no longer exists.
  conversation(conversationId: string): Observable<Conversation> {
    return this.http.get<Conversation>(`/api/chat/${encodeURIComponent(conversationId)}`);
  }
}

async function requestError(response: Response): Promise<ChatRequestError> {
  let problem: { title?: string; errors?: Record<string, string[]> } | null = null;
  try {
    problem = await response.json();
  } catch {
    // Not problem details (e.g. a proxy's error page).
  }

  const retryAfter = Number(response.headers.get('Retry-After')) || null;
  switch (response.status) {
    case 400:
      return new ChatRequestError(400, Object.values(problem?.errors ?? {})[0]?.[0] ?? 'Soru geçersiz.');
    case 429:
      return new ChatRequestError(
        429,
        `Kısa sürede çok soru sordunuz. ${retryAfter ? `${retryAfter} saniye` : 'Biraz'} sonra yeniden deneyin.`,
        retryAfter,
      );
    case 503:
      return new ChatRequestError(503, problem?.title ?? 'Şu an cevap veremiyorum, lütfen biraz sonra tekrar deneyin.');
    default:
      return new ChatRequestError(response.status, 'Şu an cevap veremiyorum, lütfen biraz sonra tekrar deneyin.');
  }
}

// The text/event-stream format: events separated by a blank line, each with an
// "event:" line (its type) and "data:" lines (here one line of JSON).
async function* readServerSentEvents(body: ReadableStream<Uint8Array>, signal: AbortSignal): AsyncGenerator<ChatEvent> {
  const reader = body.getReader();
  // stream: true keeps a character split between two chunks (ğ, ş are two bytes) intact.
  const decoder = new TextDecoder();
  let buffered = '';

  try {
    while (!signal.aborted) {
      const { value, done } = await reader.read();
      if (done) {
        return;
      }

      buffered += decoder.decode(value, { stream: true }).replace(/\r\n/g, '\n');
      let end: number;
      while ((end = buffered.indexOf('\n\n')) >= 0) {
        const event = parseEvent(buffered.slice(0, end));
        buffered = buffered.slice(end + 2);
        if (event) {
          yield event;
        }
      }
    }
  } finally {
    reader.releaseLock();
  }
}

function parseEvent(block: string): ChatEvent | null {
  let type = 'message';
  const data: string[] = [];
  for (const line of block.split('\n')) {
    if (line.startsWith('event:')) {
      type = line.slice(6).trim();
    } else if (line.startsWith('data:')) {
      data.push(line.slice(5).trimStart());
    }
  }

  if (data.length === 0) {
    return null;
  }

  return { type, ...JSON.parse(data.join('\n')) } as ChatEvent;
}

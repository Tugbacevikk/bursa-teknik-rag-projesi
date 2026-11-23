import { provideHttpClient } from '@angular/common/http';
import { TestBed } from '@angular/core/testing';
import { ChatApi, ChatEvent, ChatRequestError } from './chat';

// A server-sent events body delivered in the given byte chunks, as the network might.
function streamOf(chunks: Uint8Array[]): ReadableStream<Uint8Array> {
  return new ReadableStream({
    start(controller) {
      chunks.forEach((chunk) => controller.enqueue(chunk));
      controller.close();
    },
  });
}

function sse(events: { type: string; data: object }[]): string {
  return events.map((e) => `event: ${e.type}\ndata: ${JSON.stringify(e.data)}\n\n`).join('');
}

async function collect(api: ChatApi): Promise<ChatEvent[]> {
  const events: ChatEvent[] = [];
  for await (const event of api.ask('Sigortayı kim öder?', null, new AbortController().signal)) {
    events.push(event);
  }
  return events;
}

describe('ChatApi.ask', () => {
  let api: ChatApi;
  let fetchMock: ReturnType<typeof vi.fn>;

  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideHttpClient()] });
    api = TestBed.inject(ChatApi);
    fetchMock = vi.fn();
    vi.stubGlobal('fetch', fetchMock);
  });

  afterEach(() => vi.unstubAllGlobals());

  it('posts the question with the CSRF header the API wants from signed-in staff', async () => {
    fetchMock.mockResolvedValue(new Response(streamOf([])));

    await collect(api);

    const [url, init] = fetchMock.mock.calls[0];
    expect(url).toBe('/api/chat');
    expect(init.method).toBe('POST');
    expect(init.headers['X-Requested-With']).toBe('XMLHttpRequest');
    expect(JSON.parse(init.body)).toEqual({ question: 'Sigortayı kim öder?', conversationId: null });
  });

  // The network splits wherever it likes: inside an event, inside a line, inside a
  // two-byte Turkish letter.
  it('reads events split anywhere, including inside a letter', async () => {
    const body = new TextEncoder().encode(
      sse([
        { type: 'conversation', data: { conversationId: 'c1' } },
        { type: 'delta', data: { text: 'Öğrenci ' } },
        { type: 'delta', data: { text: 'işleri [1].' } },
        { type: 'done', data: { messageId: 'm1' } },
      ]),
    );
    const chunks: Uint8Array[] = [];
    for (let i = 0; i < body.length; i += 7) {
      chunks.push(body.slice(i, i + 7));
    }
    fetchMock.mockResolvedValue(new Response(streamOf(chunks)));

    expect(await collect(api)).toEqual([
      { type: 'conversation', conversationId: 'c1' },
      { type: 'delta', text: 'Öğrenci ' },
      { type: 'delta', text: 'işleri [1].' },
      { type: 'done', messageId: 'm1' },
    ]);
  });

  it('accepts Windows line endings', async () => {
    const body = sse([{ type: 'not_found', data: { messageId: 'm2', message: 'Bulamadım.' } }]).replaceAll('\n', '\r\n');
    fetchMock.mockResolvedValue(new Response(streamOf([new TextEncoder().encode(body)])));

    expect(await collect(api)).toEqual([{ type: 'not_found', messageId: 'm2', message: 'Bulamadım.' }]);
  });

  it('still reads a last event the stream closed without its blank line', async () => {
    const body = sse([{ type: 'delta', data: { text: 'Cevap' } }]) + 'event: done\ndata: {"messageId":"m3"}';
    fetchMock.mockResolvedValue(new Response(streamOf([new TextEncoder().encode(body)])));

    expect(await collect(api)).toEqual([
      { type: 'delta', text: 'Cevap' },
      { type: 'done', messageId: 'm3' },
    ]);
  });

  it('reports the field message of a 400', async () => {
    fetchMock.mockResolvedValue(
      Response.json({ errors: { question: ['Soru boş olamaz.'] } }, { status: 400 }),
    );

    await expect(collect(api)).rejects.toMatchObject({ status: 400, message: 'Soru boş olamaz.' });
  });

  it('says how long to wait on 429', async () => {
    fetchMock.mockResolvedValue(new Response(null, { status: 429, headers: { 'Retry-After': '42' } }));

    const error = await collect(api).catch((e: unknown) => e);

    expect(error).toBeInstanceOf(ChatRequestError);
    expect(error).toMatchObject({ status: 429, retryAfter: 42 });
    expect((error as Error).message).toContain('42 saniye');
  });

  it("shows the API's own busy message on 503", async () => {
    fetchMock.mockResolvedValue(Response.json({ title: 'Şu an çok sayıda soru geliyor.' }, { status: 503 }));

    await expect(collect(api)).rejects.toMatchObject({ status: 503, message: 'Şu an çok sayıda soru geliyor.' });
  });

  it('reports an unreachable API as status 0', async () => {
    fetchMock.mockRejectedValue(new TypeError('Failed to fetch'));

    await expect(collect(api)).rejects.toMatchObject({ status: 0 });
  });

  // A proxy's HTML error page is not problem details; the student still gets a sentence.
  it('copes with an error that is not JSON', async () => {
    fetchMock.mockResolvedValue(new Response('<html>Bad Gateway</html>', { status: 502 }));

    await expect(collect(api)).rejects.toMatchObject({ status: 502, message: expect.stringContaining('cevap veremiyorum') });
  });
});

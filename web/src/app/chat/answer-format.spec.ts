import { citedNumbers, formatAnswer } from './answer-format';

// The little Markdown the model writes (PLANNING C7), turned into blocks shown as text.
describe('formatAnswer', () => {
  it('splits paragraphs at blank lines and joins their lines', () => {
    expect(formatAnswer('Birinci\nsatır.\n\nİkinci.', 0)).toEqual([
      { kind: 'paragraph', inlines: [{ kind: 'text', text: 'Birinci satır.' }] },
      { kind: 'paragraph', inlines: [{ kind: 'text', text: 'İkinci.' }] },
    ]);
  });

  it('reads "*", "-" and numbered lists, and the paragraph around them', () => {
    const blocks = formatAnswer('Şartlar:\n* GANO 2,00\n- 6 yarıyıl\n1. Başvuru\n2) Mülakat\nSon.', 0);

    expect(blocks.map((b) => b.kind)).toEqual(['paragraph', 'list', 'paragraph']);
    expect(blocks[1]).toEqual({
      kind: 'list',
      ordered: false,
      items: [
        [{ kind: 'text', text: 'GANO 2,00' }],
        [{ kind: 'text', text: '6 yarıyıl' }],
        [{ kind: 'text', text: 'Başvuru' }],
        [{ kind: 'text', text: 'Mülakat' }],
      ],
    });
  });

  it('turns **bold** and [n] into their own pieces', () => {
    expect(formatAnswer('Son gün **25 Ağustos** [2].', 3)).toEqual([
      {
        kind: 'paragraph',
        inlines: [
          { kind: 'text', text: 'Son gün ' },
          { kind: 'bold', text: '25 Ağustos' },
          { kind: 'text', text: ' ' },
          { kind: 'cite', number: 2 },
          { kind: 'text', text: '.' },
        ],
      },
    ]);
  });

  // A number the answer was not given (the model's mistake) is not a link to nowhere.
  it('keeps citations of sources that do not exist as plain text', () => {
    const [block] = formatAnswer('Bkz. [0] ve [4] ama [3].', 3);

    expect(block).toEqual({
      kind: 'paragraph',
      inlines: [
        { kind: 'text', text: 'Bkz. ' },
        { kind: 'text', text: '[0]' },
        { kind: 'text', text: ' ve ' },
        { kind: 'text', text: '[4]' },
        { kind: 'text', text: ' ama ' },
        { kind: 'cite', number: 3 },
        { kind: 'text', text: '.' },
      ],
    });
  });

  // Nothing becomes markup: the template shows text only.
  it('leaves HTML as text', () => {
    expect(formatAnswer('<img src=x onerror=alert(1)>', 1)).toEqual([
      { kind: 'paragraph', inlines: [{ kind: 'text', text: '<img src=x onerror=alert(1)>' }] },
    ]);
  });

  it('marks a numbered list as ordered, so the steps keep their numbers', () => {
    expect(formatAnswer('1. Başvuru\n2. Mülakat', 0)).toEqual([
      {
        kind: 'list',
        ordered: true,
        items: [[{ kind: 'text', text: 'Başvuru' }], [{ kind: 'text', text: 'Mülakat' }]],
      },
    ]);
  });

  it('shows a heading as a bold line, without its "#" marks', () => {
    expect(formatAnswer('### Gerekli belgeler [1]\nKimlik.', 1)).toEqual([
      { kind: 'paragraph', inlines: [{ kind: 'bold', text: 'Gerekli belgeler ' }, { kind: 'cite', number: 1 }] },
      { kind: 'paragraph', inlines: [{ kind: 'text', text: 'Kimlik.' }] },
    ]);
  });

  it('keeps a citation inside bold a citation', () => {
    expect(formatAnswer('Son gün **25 Ağustos [2]**.', 2)).toEqual([
      {
        kind: 'paragraph',
        inlines: [
          { kind: 'text', text: 'Son gün ' },
          { kind: 'bold', text: '25 Ağustos ' },
          { kind: 'cite', number: 2 },
          { kind: 'text', text: '.' },
        ],
      },
    ]);
  });

  it('leaves "#" that is not a heading as text', () => {
    expect(formatAnswer('#5 numaralı form', 0)).toEqual([
      { kind: 'paragraph', inlines: [{ kind: 'text', text: '#5 numaralı form' }] },
    ]);
  });

  it('gives nothing for an empty answer (still streaming)', () => {
    expect(formatAnswer('', 2)).toEqual([]);
  });
});

describe('citedNumbers', () => {
  it('lists the cited sources once, in the order they first appear', () => {
    expect(citedNumbers('A [3], B [1], tekrar [3] ve [2].', 3)).toEqual([3, 1, 2]);
  });

  it('leaves out numbers outside the sources', () => {
    expect(citedNumbers('[0] [5] [2]', 4)).toEqual([2]);
  });
});

// Turns an answer's text into blocks the template can show, for the little Markdown the
// model writes (PLANNING C7): paragraphs, "* " / "- " lists, **bold** and [n] citations.
//
// No HTML is produced: the template shows each piece as text, so whatever the model writes
// can never run as markup in the page.

export type Inline =
  | { kind: 'text'; text: string }
  | { kind: 'bold'; text: string }
  // number: a source the answer was given; invalid citations stay plain text.
  | { kind: 'cite'; number: number };

export type Block = { kind: 'paragraph'; inlines: Inline[] } | { kind: 'list'; items: Inline[][] };

const listItem = /^\s*(?:[*-]|\d+[.)])\s+/;

export function formatAnswer(text: string, sourceCount: number): Block[] {
  const blocks: Block[] = [];
  let paragraph: string[] = [];
  let list: string[] = [];

  const flushParagraph = () => {
    if (paragraph.length > 0) {
      blocks.push({ kind: 'paragraph', inlines: inlines(paragraph.join(' '), sourceCount) });
      paragraph = [];
    }
  };
  const flushList = () => {
    if (list.length > 0) {
      blocks.push({ kind: 'list', items: list.map((item) => inlines(item, sourceCount)) });
      list = [];
    }
  };

  for (const line of text.split('\n')) {
    if (line.trim() === '') {
      flushParagraph();
      flushList();
    } else if (listItem.test(line)) {
      flushParagraph();
      list.push(line.replace(listItem, ''));
    } else {
      flushList();
      paragraph.push(line.trim());
    }
  }

  flushParagraph();
  flushList();
  return blocks;
}

// The source numbers an answer cites, in the order they first appear.
export function citedNumbers(text: string, sourceCount: number): number[] {
  const seen = new Set<number>();
  for (const match of text.matchAll(/\[(\d+)\]/g)) {
    const number = Number(match[1]);
    if (number >= 1 && number <= sourceCount) {
      seen.add(number);
    }
  }
  return [...seen];
}

function inlines(text: string, sourceCount: number): Inline[] {
  const result: Inline[] = [];
  // **bold** or [n]; everything between is text.
  const pattern = /\*\*(.+?)\*\*|\[(\d+)\]/g;
  let last = 0;

  for (const match of text.matchAll(pattern)) {
    if (match.index > last) {
      result.push({ kind: 'text', text: text.slice(last, match.index) });
    }

    if (match[1] !== undefined) {
      result.push({ kind: 'bold', text: match[1] });
    } else {
      const number = Number(match[2]);
      result.push(
        number >= 1 && number <= sourceCount ? { kind: 'cite', number } : { kind: 'text', text: match[0] },
      );
    }

    last = match.index + match[0].length;
  }

  if (last < text.length) {
    result.push({ kind: 'text', text: text.slice(last) });
  }

  return result;
}

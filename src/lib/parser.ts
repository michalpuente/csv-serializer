import iconv from 'iconv-lite';
import jschardet from 'jschardet';
import { parse } from 'csv-parse/sync';
import { normalizeIdentifier, uniqueIdentifiers } from './sql.js';
import { normalizeTextValue, repairPolishMojibake, suspiciousScore } from './normalization.js';

export type HeaderColumn = {
  original: string;
  key: string;
  isDateLike: boolean;
  isAmountLike: boolean;
};

export type ParsedPreview = {
  encoding: string;
  delimiter: string;
  headerLineIndex: number;
  headers: HeaderColumn[];
  sampleRows: string[][];
  normalizedText: string;
};

const KNOWN_ENCODINGS = ['utf-8', 'windows-1250', 'iso-8859-2', 'cp1250'];
const COMMON_DELIMITERS = [';', ',', '\t'];

function decodeCandidates(buffer: Buffer): Array<{ encoding: string; text: string }> {
  const detected = jschardet.detect(buffer);
  const candidateEncodings = [detected.encoding?.toLowerCase(), ...KNOWN_ENCODINGS].filter(
    (v): v is string => Boolean(v),
  );
  const uniq = Array.from(new Set(candidateEncodings));

  return uniq.map((encoding) => {
    try {
      return { encoding, text: iconv.decode(buffer, encoding as iconv.Encoding) };
    } catch {
      return { encoding, text: buffer.toString('utf-8') };
    }
  });
}

function chooseBestDecoding(buffer: Buffer): { encoding: string; text: string } {
  const candidates = decodeCandidates(buffer);
  candidates.sort((a, b) => suspiciousScore(a.text) - suspiciousScore(b.text));
  return candidates[0] ?? { encoding: 'utf-8', text: buffer.toString('utf-8') };
}

function detectDelimiter(lines: string[]): string {
  const scored = COMMON_DELIMITERS.map((delimiter) => {
    const score = lines
      .slice(0, 40)
      .reduce((acc, line) => acc + (line.split(delimiter).length > 2 ? 1 : 0), 0);
    return { delimiter, score };
  }).sort((a, b) => b.score - a.score);

  return scored[0]?.delimiter ?? ';';
}

export function detectHeaderLine(lines: string[], delimiter: string): number {
  for (let i = 0; i < lines.length; i += 1) {
    const line = lines[i].trim();
    if (!line) continue;
    const cols = line.split(delimiter).map((cell) => cell.trim());
    if (cols.length < 4) continue;
    const hashCount = cols.filter((c) => c.startsWith('#')).length;
    const bankingNames = cols.filter((c) => /data|kwota|saldo|tytul|tytuł|opis|konto|nadawca|odbiorca/i.test(c)).length;
    if (hashCount >= 2 || bankingNames >= 2) {
      return i;
    }
  }
  return -1;
}

export function parsePreviewFromBuffer(buffer: Buffer): ParsedPreview {
  const decoded = chooseBestDecoding(buffer);
  const normalizedText = repairPolishMojibake(decoded.text);
  const lines = normalizedText.split(/\r?\n/);
  const delimiter = detectDelimiter(lines);
  const headerLineIndex = detectHeaderLine(lines, delimiter);

  if (headerLineIndex < 0) {
    throw new Error('Header row not found');
  }

  const parsed = parse(lines.slice(headerLineIndex).join('\n'), {
    delimiter,
    relax_column_count: true,
    skip_empty_lines: true,
    trim: true,
  }) as string[][];

  const rawHeaders = parsed[0] ?? [];
  const headerKeys = uniqueIdentifiers(rawHeaders.map((h) => normalizeIdentifier(h, 'col')));

  const headers = rawHeaders.map((original, index) => {
    const normalized = normalizeTextValue(original.replace(/^#+/, '')) ?? `Column ${index + 1}`;
    return {
      original: normalized,
      key: headerKeys[index],
      isDateLike: /data|date/i.test(normalized),
      isAmountLike: /kwota|saldo|amount|pln|eur/i.test(normalized),
    };
  });

  return {
    encoding: decoded.encoding,
    delimiter,
    headerLineIndex,
    headers,
    sampleRows: parsed.slice(1, 6),
    normalizedText,
  };
}

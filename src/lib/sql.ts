const POLISH_MAP: Record<string, string> = {
  ą: 'a', ć: 'c', ę: 'e', ł: 'l', ń: 'n', ó: 'o', ś: 's', ź: 'z', ż: 'z',
  Ą: 'a', Ć: 'c', Ę: 'e', Ł: 'l', Ń: 'n', Ó: 'o', Ś: 's', Ź: 'z', Ż: 'z',
  œ: 's', Œ: 's', Ÿ: 'z', '£': 'l', '³': 'l', '¹': 'a', '¥': 'a', '¿': 'z', '¯': 'z', ê: 'e', Ê: 'e',
  æ: 'c', Æ: 'c', ñ: 'n', Ñ: 'n',
};

function asciiTransliterate(input: string): string {
  return Array.from(input)
    .map((ch) => POLISH_MAP[ch] ?? ch)
    .join('');
}

export function normalizeIdentifier(raw: string, fallback = 'col'): string {
  const cleaned = asciiTransliterate(raw.trim().replace(/^#+/, ''))
    .toLowerCase()
    .replace(/[^a-z0-9]+/g, '_')
    .replace(/^_+|_+$/g, '')
    .replace(/_+/g, '_');

  const base = cleaned || fallback;
  return /^[a-z_]/.test(base) ? base : `${fallback}_${base}`;
}

export function normalizeTableName(raw: string): string {
  if (!raw.trim()) {
    throw new Error('Invalid table name');
  }
  const name = normalizeIdentifier(raw, 'dataset');
  if (!/^[a-z_][a-z0-9_]{1,62}$/.test(name)) {
    throw new Error('Invalid table name');
  }
  return name;
}

export function uniqueIdentifiers(items: string[]): string[] {
  const used = new Map<string, number>();
  return items.map((item) => {
    const count = used.get(item) ?? 0;
    used.set(item, count + 1);
    return count === 0 ? item : `${item}_${count + 1}`;
  });
}

export function quoteIdentifier(identifier: string): string {
  if (!/^[a-z_][a-z0-9_]{0,62}$/.test(identifier)) {
    throw new Error(`Unsafe SQL identifier: ${identifier}`);
  }
  return `"${identifier}"`;
}

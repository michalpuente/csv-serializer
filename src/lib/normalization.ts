import crypto from 'node:crypto';

const REPAIR_MAP: Array<[RegExp, string]> = [
  [/œ/g, 'ś'],
  [/Œ/g, 'Ś'],
  [/Ÿ/g, 'ź'],
  [/£/g, 'Ł'],
  [/³/g, 'ł'],
  [/¹/g, 'ą'],
  [/¥/g, 'Ą'],
  [/¿/g, 'ż'],
  [/¯/g, 'Ż'],
  [/ê/g, 'ę'],
  [/Ê/g, 'Ę'],
  [/æ/g, 'ć'],
  [/Æ/g, 'Ć'],
  [/ñ/g, 'ń'],
  [/Ñ/g, 'Ń'],
];

export function repairPolishMojibake(input: string): string {
  let result = input;
  for (const [pattern, replacement] of REPAIR_MAP) {
    result = result.replace(pattern, replacement);
  }
  return result;
}

export function normalizeTextValue(value: string): string | null {
  const repaired = repairPolishMojibake(value).normalize('NFC').trim();
  if (!repaired || /^['"]{2}$/.test(repaired)) {
    return null;
  }
  return repaired.replace(/\s+/g, ' ');
}

export function suspiciousScore(text: string): number {
  const legacy = (text.match(/[œŸ£ê¹¿³¥]/g) ?? []).length;
  const brokenUtf8 = (text.match(/[ÃÂÄ]/g) ?? []).length;
  const replacement = (text.match(/�/g) ?? []).length;
  return legacy + brokenUtf8 * 3 + replacement * 5;
}

export function parseDate(value: string | null): string | null {
  if (!value) return null;
  const simple = value.match(/^(\d{4})-(\d{2})-(\d{2})$/);
  if (simple) return value;
  const pl = value.match(/^(\d{2})[./-](\d{2})[./-](\d{4})$/);
  if (pl) return `${pl[3]}-${pl[2]}-${pl[1]}`;
  return null;
}

export function parseAmount(value: string | null): string | null {
  if (!value) return null;
  const normalized = value
    .replace(/\s+/g, '')
    .replace(/\.(?=\d{3}(\D|$))/g, '')
    .replace(',', '.');

  if (!/^-?\d+(\.\d+)?$/.test(normalized)) return null;
  return Number(normalized).toFixed(2);
}

export function buildRowHash(values: Array<string | null>): string {
  return crypto
    .createHash('sha256')
    .update(JSON.stringify(values))
    .digest('hex');
}

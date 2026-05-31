import { describe, expect, it } from 'vitest';
import { repairPolishMojibake } from './normalization.js';
import { parsePreviewFromBuffer, detectHeaderLine } from './parser.js';
import { normalizeIdentifier, normalizeTableName, uniqueIdentifiers } from './sql.js';

describe('polish text repair', () => {
  it('repairs known mojibake samples', () => {
    expect(repairPolishMojibake('Bankowoœæ')).toBe('Bankowość');
    expect(repairPolishMojibake('£ódŸ')).toBe('Łódź');
    expect(repairPolishMojibake('MICHA£')).toBe('MICHAŁ');
    expect(repairPolishMojibake('nastêpnej')).toBe('następnej');
    expect(repairPolishMojibake('Tytu³')).toBe('Tytuł');
  });
});

describe('identifier normalization', () => {
  it('normalizes and deduplicates identifiers', () => {
    expect(normalizeIdentifier('#Data księgowania')).toBe('data_ksiegowania');
    expect(normalizeIdentifier('#Nadawca/Odbiorca')).toBe('nadawca_odbiorca');
    expect(uniqueIdentifiers(['kolumna', 'kolumna'])).toEqual(['kolumna', 'kolumna_2']);
  });

  it('normalizes table names safely', () => {
    expect(normalizeTableName(' Moja Tabela 2026 ')).toBe('moja_tabela_2026');
    expect(() => normalizeTableName('')).toThrow();
  });
});

describe('header detection', () => {
  it('finds first banking style header row', () => {
    const lines = [
      '#Podsumowanie obrotów',
      '',
      '#Data księgowania;#Data operacji;#Opis operacji;#Kwota;#Saldo po operacji',
      '2026-01-01;2026-01-01;Test;12,00;100,00',
    ];

    expect(detectHeaderLine(lines, ';')).toBe(2);
  });

  it('parses preview from malformed bank file', () => {
    const content = Buffer.from(
      '#Podsumowanie obrotów na rachunku\n\n#Data księgowania;#Tytu³;#Nadawca/Odbiorca;#Kwota\n01.01.2026;Bankowoœæ;£ódŸ;1 234,56',
      'utf-8',
    );

    const preview = parsePreviewFromBuffer(content);
    expect(preview.headers.map((h) => h.key)).toEqual(['data_ksiegowania', 'tytul', 'nadawca_odbiorca', 'kwota']);
    expect(preview.sampleRows[0]?.[1]).toContain('Bankowo');
  });
});

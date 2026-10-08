export interface CsvTable {
  columns: string[];
  rows: Record<string, string>[];
}

/** Client-side mirror of the server CSV parser (header row, quoted fields, tab-separated auto-detect). */
export function parseCsv(text: string | null | undefined): CsvTable {
  if (!text || !text.trim()) return { columns: [], rows: [] };
  const firstLine = text.split(/\r?\n/, 1)[0];
  const delimiter = firstLine.includes('\t') && !firstLine.includes(',') ? '\t' : ',';

  const records: string[][] = [];
  let field = '';
  let record: string[] = [];
  let inQuotes = false;
  for (let i = 0; i < text.length; i++) {
    const c = text[i];
    if (inQuotes) {
      if (c === '"') {
        if (text[i + 1] === '"') {
          field += '"';
          i++;
        } else {
          inQuotes = false;
        }
      } else {
        field += c;
      }
    } else if (c === '"' && field.length === 0) {
      inQuotes = true;
    } else if (c === delimiter) {
      record.push(field);
      field = '';
    } else if (c === '\r' || c === '\n') {
      if (c === '\r' && text[i + 1] === '\n') i++;
      record.push(field);
      field = '';
      records.push(record);
      record = [];
    } else {
      field += c;
    }
  }
  if (field.length > 0 || record.length > 0) {
    record.push(field);
    records.push(record);
  }

  const nonEmpty = records.filter((r) => !(r.length === 1 && !r[0].trim()));
  if (nonEmpty.length === 0) return { columns: [], rows: [] };
  const columns = nonEmpty[0].map((c) => c.trim());
  const rows = nonEmpty.slice(1).map((r) => {
    const row: Record<string, string> = {};
    columns.forEach((col, i) => {
      if (col && !(col in row)) row[col] = r[i] ?? '';
    });
    return row;
  });
  return { columns, rows };
}

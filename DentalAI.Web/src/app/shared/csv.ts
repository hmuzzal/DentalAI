type Cell = string | number | null | undefined;

/** One CSV field: quoted when needed; cells that a spreadsheet would run as a formula get a leading apostrophe. */
function field(value: Cell): string {
  if (value === null || value === undefined) return '';
  let text = String(value);
  if (typeof value === 'string' && /^[=+\-@\t\r]/.test(text)) text = `'${text}`;
  return /[",\r\n]/.test(text) ? `"${text.replace(/"/g, '""')}"` : text;
}

/** Builds a CSV (with a BOM so Excel reads UTF-8) and makes the browser download it. */
export function downloadCsv(fileName: string, header: string[], rows: Cell[][]): void {
  const text = [header, ...rows].map((row) => row.map(field).join(',')).join('\r\n');
  const url = URL.createObjectURL(new Blob(['﻿' + text], { type: 'text/csv;charset=utf-8' }));
  const link = document.createElement('a');
  link.href = url;
  link.download = fileName;
  link.click();
  setTimeout(() => URL.revokeObjectURL(url), 0);
}

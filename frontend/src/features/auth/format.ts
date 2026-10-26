/** "JBSW Y3DP EHPK 3PXP" style grouping for manual entry. */
export function formatSharedKey(key: string): string {
  return key
    .replace(/\s+/g, '')
    .toUpperCase()
    .replace(/(.{4})/g, '$1 ')
    .trim()
}

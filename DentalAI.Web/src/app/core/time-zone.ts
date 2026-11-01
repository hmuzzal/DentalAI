/**
 * Human-readable name for an IANA time zone, e.g. "America/New_York" -> "Eastern Time".
 * Falls back to the raw ID if the browser doesn't recognise it.
 */
export function timeZoneLabel(timeZone: string | null | undefined, date = new Date()): string | null {
  if (!timeZone) {
    return null;
  }

  try {
    const parts = new Intl.DateTimeFormat('en-US', { timeZone, timeZoneName: 'longGeneric' }).formatToParts(date);
    return parts.find((part) => part.type === 'timeZoneName')?.value ?? timeZone;
  } catch {
    return timeZone;
  }
}

/**
 * Human-readable name for an IANA time zone, e.g. "America/New_York" -> "Eastern Time".
 * Falls back to the raw ID if the browser doesn't recognise it.
 */
export function timeZoneLabel(timeZone: string | null | undefined, date = new Date()): string | null {
  if (!timeZone) {
    return null;
  }

  if (timeZone === 'UTC' || timeZone === 'Etc/UTC') {
    return 'UTC';
  }

  try {
    const parts = new Intl.DateTimeFormat('en-US', { timeZone, timeZoneName: 'longGeneric' }).formatToParts(date);
    return parts.find((part) => part.type === 'timeZoneName')?.value ?? timeZone;
  } catch {
    return timeZone;
  }
}

/** The zone to schedule in: the clinic's when known, otherwise the browser's. */
export function effectiveTimeZone(timeZone: string | null | undefined): string {
  if (timeZone) {
    try {
      new Intl.DateTimeFormat('en-US', { timeZone });
      return timeZone;
    } catch {
      // Unknown to this browser; fall through.
    }
  }
  return Intl.DateTimeFormat().resolvedOptions().timeZone;
}

/** Formats a UTC instant as wall-clock time in the given zone. */
export function formatInZone(value: string | Date, timeZone: string, options: Intl.DateTimeFormatOptions): string {
  return new Intl.DateTimeFormat('en-US', { timeZone, ...options }).format(typeof value === 'string' ? new Date(value) : value);
}

/** Calendar date ("yyyy-MM-dd") of an instant in the given zone. */
export function dateKeyInZone(value: string | Date, timeZone: string): string {
  // en-CA formats as yyyy-MM-dd.
  return new Intl.DateTimeFormat('en-CA', { timeZone, year: 'numeric', month: '2-digit', day: '2-digit' })
    .format(typeof value === 'string' ? new Date(value) : value);
}

/** Hour of day (0–23) of an instant in the given zone. */
export function hourInZone(value: string | Date, timeZone: string): number {
  const hour = new Intl.DateTimeFormat('en-US', { timeZone, hour: '2-digit', hourCycle: 'h23' })
    .format(typeof value === 'string' ? new Date(value) : value);
  return Number(hour);
}

/**
 * Formats an appointment time in its clinic's zone. When that differs from the zone the page is
 * showing (e.g. a platform admin looking at another clinic), the zone abbreviation is appended.
 */
export function formatAppointmentTime(
  value: string | Date,
  appointmentZone: string,
  viewerZone: string,
  options: Intl.DateTimeFormatOptions
): string {
  return formatInZone(value, appointmentZone,
    appointmentZone === viewerZone ? options : { ...options, timeZoneName: 'short' });
}

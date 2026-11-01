import { DoctorAvailability } from '../../core/models/scheduling.models';

/** Sunday-first, matching US calendars and the Appointments page. Values are the API's DayOfWeek names. */
export const WEEK_DAYS = ['Sunday', 'Monday', 'Tuesday', 'Wednesday', 'Thursday', 'Friday', 'Saturday'] as const;
export type WeekDay = (typeof WEEK_DAYS)[number];
export const WORK_WEEK: WeekDay[] = ['Monday', 'Tuesday', 'Wednesday', 'Thursday', 'Friday'];

/** A wall-clock range in the clinic's time zone, "HH:mm" 24-hour. */
export interface TimeRange {
  start: string;
  end: string;
}

export interface DaySchedule {
  day: WeekDay;
  open: boolean;
  hours: TimeRange[];
  breaks: TimeRange[];
}

export interface TimeOff {
  /** yyyy-MM-dd */
  startDate: string;
  endDate: string;
  reason: string;
}

export interface WeeklySchedule {
  days: DaySchedule[];
  timeOff: TimeOff[];
}

export const DEFAULT_HOURS: TimeRange = { start: '09:00', end: '17:00' };
export const DEFAULT_BREAK: TimeRange = { start: '12:00', end: '13:00' };

/** "09:00:00" or "09:00" → "09:00". */
export function hhmm(value: string): string {
  return value.slice(0, 5);
}

export function minutes(value: string): number {
  const [h, m] = value.split(':').map(Number);
  return h * 60 + m;
}

export function fromAvailability(availability: DoctorAvailability): WeeklySchedule {
  const days = WEEK_DAYS.map<DaySchedule>((day) => {
    const hours = availability.schedules
      .filter((s) => s.dayOfWeek === day && s.isWorking)
      .map((s) => ({ start: hhmm(s.startTime), end: hhmm(s.endTime) }))
      .sort((a, b) => a.start.localeCompare(b.start));
    const breaks = availability.breaks
      .filter((b) => b.dayOfWeek === day)
      .map((b) => ({ start: hhmm(b.startTime), end: hhmm(b.endTime) }))
      .sort((a, b) => a.start.localeCompare(b.start));
    return { day, open: hours.length > 0, hours: hours.length ? hours : [{ ...DEFAULT_HOURS }], breaks };
  });
  const timeOff = availability.leaves
    .map((l) => ({ startDate: l.startDate, endDate: l.endDate, reason: l.reason }))
    .sort((a, b) => a.startDate.localeCompare(b.startDate));
  return { days, timeOff };
}

/** The API body. Closed days send nothing (their hours are kept in the editor only). */
export function toAvailability(schedule: WeeklySchedule): Omit<DoctorAvailability, 'doctorId'> {
  const open = schedule.days.filter((d) => d.open);
  return {
    schedules: open.flatMap((d) => d.hours.map((h) => ({
      dayOfWeek: d.day, startTime: `${h.start}:00`, endTime: `${h.end}:00`, isWorking: true
    }))),
    breaks: open.flatMap((d) => d.breaks.map((b) => ({
      dayOfWeek: d.day, startTime: `${b.start}:00`, endTime: `${b.end}:00`
    }))),
    leaves: schedule.timeOff.map((t) => ({ startDate: t.startDate, endDate: t.endDate, reason: t.reason.trim() }))
  };
}

/**
 * Problems per day (and for time off), mirroring the API's rules so the user sees them before saving:
 * end after start, working periods don't overlap, breaks sit inside working hours, time off has dates and a reason.
 */
export function validate(schedule: WeeklySchedule): { days: Record<string, string[]>; timeOff: string[][]; count: number } {
  const days: Record<string, string[]> = {};
  let count = 0;

  for (const day of schedule.days) {
    if (!day.open) continue;
    const problems: string[] = [];
    const ranges = [...day.hours, ...day.breaks];
    if (ranges.some((r) => !r.start || !r.end)) problems.push('Enter a start and end time.');
    else {
      if (day.hours.some((h) => minutes(h.end) <= minutes(h.start))) problems.push('Working hours must end after they start.');
      if (day.breaks.some((b) => minutes(b.end) <= minutes(b.start))) problems.push('Breaks must end after they start.');
      const sorted = [...day.hours].sort((a, b) => minutes(a.start) - minutes(b.start));
      if (sorted.some((h, i) => i > 0 && minutes(h.start) < minutes(sorted[i - 1].end))) {
        problems.push('Working periods overlap.');
      }
      if (day.breaks.some((b) => !day.hours.some((h) => minutes(b.start) >= minutes(h.start) && minutes(b.end) <= minutes(h.end)))) {
        problems.push('Each break must fall inside working hours.');
      }
    }
    if (problems.length) {
      days[day.day] = problems;
      count += problems.length;
    }
  }

  const timeOff = schedule.timeOff.map((t) => {
    const problems: string[] = [];
    if (!t.startDate || !t.endDate) problems.push('Choose the dates.');
    else if (t.endDate < t.startDate) problems.push('The end date is before the start date.');
    if (!t.reason.trim()) problems.push('Enter a reason.');
    count += problems.length;
    return problems;
  });

  return { days, timeOff, count };
}

/** Total scheduled hours in a week, minus breaks (open days only). */
export function weeklyHours(schedule: WeeklySchedule): number {
  return schedule.days.filter((d) => d.open).reduce((total, d) => {
    const work = d.hours.reduce((s, h) => s + Math.max(0, minutes(h.end) - minutes(h.start)), 0);
    const rest = d.breaks.reduce((s, b) => s + Math.max(0, minutes(b.end) - minutes(b.start)), 0);
    return total + Math.max(0, work - rest);
  }, 0) / 60;
}

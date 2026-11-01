import { Pipe, PipeTransform } from '@angular/core';
import { format, isValid, parseISO } from 'date-fns';

/**
 * Formats a date-only value from the API ("2026-11-02": a date of birth, an invoice date) as that calendar date,
 * whatever the browser's time zone. Use it instead of the date pipe for date-only values: the date pipe with a
 * time-zone argument shifts them a day for users east or west of that zone.
 *
 *   {{ patient.dateOfBirth | dateOnly: 'MM/dd/yyyy' }}   (date-fns format tokens)
 */
@Pipe({ name: 'dateOnly', standalone: true })
export class DateOnlyPipe implements PipeTransform {
  transform(value: string | null | undefined, pattern = 'MMM d, yyyy'): string {
    if (!value) return '';
    const date = parseISO(value.slice(0, 10)); // date-only → local midnight of that date
    return isValid(date) ? format(date, pattern) : value;
  }
}

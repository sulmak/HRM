export const GENDERS = ['Male', 'Female', 'Other'];
export const EMPLOYMENT_STATUS = ['Probation', 'Confirmed', 'Resigned', 'Terminated'];
export const LEAVE_TYPES = ['Casual', 'Sick', 'Earned', 'Maternity', 'Paternity', 'Unpaid'];
export const WEEK_DAYS = ['Sunday', 'Monday', 'Tuesday', 'Wednesday', 'Thursday', 'Friday', 'Saturday'];
export const MONTH_NAMES = ['January', 'February', 'March', 'April', 'May', 'June', 'July', 'August',
  'September', 'October', 'November', 'December'];

/** yyyy-MM-dd in the browser's local time zone (toISOString would shift the day). */
export function ymd(d: Date): string {
  return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`;
}

export const today = () => ymd(new Date());
export const monthStart = (y: number, m: number) => ymd(new Date(y, m - 1, 1));
export const monthEnd = (y: number, m: number) => ymd(new Date(y, m, 0));

/** "09:30" -> "09:30:00" for the API's TimeOnly; empty -> null. */
export const toTime = (v?: string | null) => (!v ? null : v.length === 5 ? v + ':00' : v);
/** "09:30:00" -> "09:30" for <input type="time">. */
export const hm = (v?: string | null) => (v ? v.slice(0, 5) : '');
/** '' / undefined -> null so optional fields reach the API as null. */
export const nz = (v: any) => (v === '' || v === undefined ? null : v);

export function badge(status: string): string {
  switch (status) {
    case 'Published': case 'Approved': case 'Selected': case 'Present': case 'Joined': case 'Paid': case 'Confirmed':
      return 'b-green';
    case 'Rejected': case 'Absent': case 'Cancelled': case 'Terminated': case 'Resigned':
      return 'b-red';
    case 'Late': case 'HalfDay': case 'Pending': case 'Draft': case 'Probation': case 'Finalized':
      return 'b-amber';
    case 'Closed': case 'WeeklyOff': case 'Holiday':
      return 'b-gray';
    default:
      return 'b-blue';
  }
}

// Formats an instant as the browser-local wall-clock value a datetime-local input expects.
// Seconds are dropped, so the value never lands later than the instant it came from.
export const toLocalInput = (date: string | Date) => {
  const value = new Date(date);
  const local = new Date(value.getTime() - value.getTimezoneOffset() * 60000);
  return local.toISOString().slice(0, 16);
};

// Scheduled work (follow-ups, site visits) must be in the future; logged activity must not be.
export const oneHourFromNow = () => new Date(Date.now() + 60 * 60 * 1000);

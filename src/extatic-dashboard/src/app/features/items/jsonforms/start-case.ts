/** Converts a property name into a spaced, capitalized label ("firstName" -> "First Name"). */
export const startCase = (value: string): string =>
  value
    .replace(/[_\-.\s]+/g, ' ')
    .replace(/([a-z0-9])([A-Z])/g, '$1 $2')
    .split(' ')
    .filter(Boolean)
    .map((word) => word.charAt(0).toUpperCase() + word.slice(1))
    .join(' ');

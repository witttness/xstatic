/**
 * Compares two dependency arrays by reference, like React's `useMemo` deps.
 *
 * Adapted from EclipseSource JSON Forms (MIT), see
 * https://github.com/eclipsesource/jsonforms
 */
export const depsChanged = (
  previous: unknown[] | undefined,
  next: unknown[],
): boolean =>
  previous === undefined ||
  previous.length !== next.length ||
  next.some((dep, index) => dep !== previous[index]);

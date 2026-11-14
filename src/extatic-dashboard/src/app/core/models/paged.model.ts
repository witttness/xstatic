/** Matches the API's PagedResult<T> record (Dtos/ItemDtos.cs), serialised in snake_case. */
export interface PagedResult<T> {
  items: T[];
  total: number;
  page: number;
  page_size: number;
}

/** Mirrors Features/Search/SearchDtos.cs (snake_case JSON). */

export type SearchSource = 'page' | 'item';
export type ReindexStatus = 'pending' | 'running' | 'completed' | 'failed';

export interface ReindexJob {
  id: string;
  collection_id: string | null;
  status: ReindexStatus;
  processed: number;
  total: number;
  error: string | null;
  created_at: string;
  started_at: string | null;
  completed_at: string | null;
}

export interface SearchOverview {
  enabled: boolean;
  public_search_enabled: boolean;
  public_id: string;
  total: number;
  pages: number;
  items: number;
  max_documents: number;
  last_indexed_at: string | null;
  enabled_collections: number;
  latest_job: ReindexJob | null;
}

export interface SearchSettings {
  enabled: boolean;
  default_language: string;
  max_documents: number;
  public_search_enabled: boolean;
  available_languages: string[];
}

export interface CollectionConfig {
  collection_id: string;
  collection_slug: string;
  collection_name: string;
  enabled: boolean;
  title_pointer: string;
  body_pointers: string[];
  tags_pointer: string | null;
  url_template: string | null;
  section: string | null;
  language: string;
  document_count: number;
  updated_at: string;
}

export interface PutCollectionConfigRequest {
  enabled: boolean;
  title_pointer: string;
  body_pointers: string[];
  tags_pointer: string | null;
  url_template: string | null;
  section: string | null;
  language: string | null;
}

export interface CollectionConfigSaveResult {
  config: CollectionConfig;
  reindex_job: ReindexJob | null;
}

export interface SearchDocumentSummary {
  id: string;
  source: SearchSource;
  external_id: string;
  title: string;
  url: string | null;
  section: string | null;
  tags: string[];
  language: string;
  collection: string | null;
  item_id: string | null;
  body_length: number;
  published_at: string | null;
  indexed_at: string;
}

export interface SearchHit {
  id: string;
  source: SearchSource;
  url: string | null;
  title: string;
  summary: string | null;
  /** HTML-escaped text whose only markup is <mark>…</mark>. Render with SearchSnippetComponent. */
  snippet: string;
  section: string | null;
  tags: string[];
  metadata: Record<string, unknown>;
  published_at: string | null;
  collection: string | null;
  item_id: string | null;
  score: number;
}

export interface SearchResponse {
  query: string;
  items: SearchHit[];
  total: number;
  page: number;
  page_size: number;
}

export interface QueryStat {
  query: string;
  count: number;
  average_results: number;
  last_searched_at: string;
}

export interface SearchAnalytics {
  total_queries: number;
  top_queries: QueryStat[];
  zero_result_queries: QueryStat[];
}

/** Same allowlist as the API (SearchLanguages.All). */
export const SEARCH_LANGUAGES = [
  'simple', 'arabic', 'armenian', 'basque', 'catalan', 'danish', 'dutch', 'english', 'estonian',
  'finnish', 'french', 'german', 'greek', 'hindi', 'hungarian', 'indonesian', 'irish', 'italian',
  'lithuanian', 'nepali', 'norwegian', 'portuguese', 'romanian', 'russian', 'serbian', 'spanish',
  'swedish', 'tamil', 'turkish', 'yiddish',
];

export const INPUT_CLASS =
  'w-full bg-background border border-border px-3 py-2 text-sm text-text-primary focus:border-accent focus:outline-none';
export const LABEL_CLASS = 'block text-xs font-medium text-text-muted uppercase tracking-wider mb-1';

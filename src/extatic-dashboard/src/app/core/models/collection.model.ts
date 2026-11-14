export interface Collection {
  id: string;
  app_id: string;
  name: string;
  slug: string;
  schema: string | null;
  attachments_enabled: boolean;
  allowed_attachment_types: string[];
  created_at: string;
  updated_at: string;
  /** Schema Library provenance: the library schema/version the schema was copied from. */
  source_schema_id?: string | null;
  source_schema_version?: number | null;
  /** Null when the source schema was deleted or is no longer visible to this app. */
  source_schema_name?: string | null;
  source_schema_latest_version?: number | null;
}

export interface CreateCollectionRequest {
  name: string;
  slug: string;
  schema?: string | null;
  attachments_enabled?: boolean;
  allowed_attachment_types?: string[];
  source_schema_id?: string | null;
  source_schema_version?: number | null;
}

export interface UpdateCollectionRequest {
  name?: string;
  schema?: string | null;
  attachments_enabled?: boolean;
  allowed_attachment_types?: string[];
  source_schema_id?: string | null;
  source_schema_version?: number | null;
  /** Clears provenance (the copied schema stays). */
  detach_source_schema?: boolean;
}

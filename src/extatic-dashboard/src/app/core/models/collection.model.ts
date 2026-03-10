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
}

export interface CreateCollectionRequest {
  name: string;
  slug: string;
  schema?: string | null;
  attachments_enabled?: boolean;
  allowed_attachment_types?: string[];
}

export interface UpdateCollectionRequest {
  name?: string;
  schema?: string | null;
  attachments_enabled?: boolean;
  allowed_attachment_types?: string[];
}

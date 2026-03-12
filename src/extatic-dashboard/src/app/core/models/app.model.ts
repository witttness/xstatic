export interface App {
  id: string;
  name: string;
  slug: string;
  public_id: string;
  allowed_origins: string[];
  max_file_size_mb: number;
  max_attachments_per_item: number;
  storage_quota_gb: number;
  created_at: string;
  updated_at: string;
}

export interface CreateAppRequest {
  name: string;
  slug: string;
}

export interface CreateAppResponse {
  app: App;
  api_key: string;
}

export interface UpdateAppRequest {
  name?: string;
  allowed_origins?: string[];
  max_file_size_mb?: number;
  max_attachments_per_item?: number;
  storage_quota_gb?: number;
}

export interface RegenerateApiKeyResponse {
  api_key: string;
}

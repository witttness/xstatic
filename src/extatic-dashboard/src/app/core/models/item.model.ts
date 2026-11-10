export interface Item {
  id: string;
  collection_id: string;
  app_user_id: string | null;
  data: unknown;
  version: number;
  created_at: string;
  updated_at: string;
  attachments: unknown[] | null;
}

export interface PagedItems {
  items: Item[];
  total: number;
  page: number;
  page_size: number;
}

export interface CreateItemRequest {
  data: Record<string, unknown>;
}

export interface UpdateItemRequest extends CreateItemRequest {
  version: number;
}

export interface ApiProblem {
  status: number;
  title: string;
  detail?: string;
  errors?: { errors?: { pointer: string; message: string }[] };
}

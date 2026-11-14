/** Platform API shapes for comments (snake_case, as serialised by the API). */
export type CommentStatus = 'pending' | 'approved' | 'rejected' | 'spam';
export type ModerationAction = 'approve' | 'reject' | 'spam';

export interface AdminComment {
  id: string;
  page_key: string;
  page_url: string | null;
  page_title: string | null;
  parent_id: string | null;
  depth: number;
  app_user_id: string | null;
  author_name: string;
  author_email: string | null;
  body: string;
  status: CommentStatus;
  is_deleted: boolean;
  user_agent: string | null;
  created_at: string;
  edited_at: string | null;
  moderated_at: string | null;
  moderated_by_user_id: string | null;
}

export interface CommentSettings {
  enabled: boolean;
  moderation_mode: 'all' | 'anonymous_only' | 'none';
  allow_anonymous: boolean;
  require_email_for_anonymous: boolean;
  max_length: number;
  max_depth: number;
  auto_close_after_days: number | null;
  blocked_terms: string[];
  blocked_term_action: 'pending' | 'spam';
  max_links: number;
  notify_emails: string[];
  expose_avatar_hash: boolean;
  updated_at?: string | null;
}

export interface CommentPage {
  page_key: string;
  page_url: string | null;
  page_title: string | null;
  is_closed: boolean;
  total: number;
  approved: number;
  pending: number;
  last_comment_at: string | null;
}

export interface CommentStats {
  pending: number;
  approved: number;
  rejected: number;
  spam: number;
}

export interface BulkModerateResult {
  updated: number;
  not_found: number;
}

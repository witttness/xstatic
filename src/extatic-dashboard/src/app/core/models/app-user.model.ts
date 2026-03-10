export interface AppUser {
  id: string;
  app_id: string;
  provider: string;
  email: string | null;
  display_name: string | null;
  avatar_url: string | null;
  last_login_at: string | null;
  created_at: string;
}

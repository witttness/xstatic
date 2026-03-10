// 0 = Viewer, 1 = Editor, 2 = Admin (Owner is implicit, no Collaborator record)
export type CollaboratorRole = 0 | 1 | 2;

export const CollaboratorRoleLabel: Record<CollaboratorRole, string> = {
  0: 'Viewer',
  1: 'Editor',
  2: 'Admin',
};

export interface Collaborator {
  id: string;
  app_id: string;
  user_id: string;
  user_email: string;
  user_name: string;
  role: CollaboratorRole;
  accepted_at: string | null;
  created_at: string;
}

export interface InviteCollaboratorRequest {
  email: string;
  role: CollaboratorRole;
}

export interface UpdateCollaboratorRoleRequest {
  role: CollaboratorRole;
}

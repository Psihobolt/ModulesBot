import { api } from '../../platform/api';

export type CreateGroupRequest = { name: string };
export type RenameGroupRequest = { name: string };
export type GroupSummaryDto = { id: string; name: string; memberCount: number; isOwner: boolean };
export type MemberDto = {
  userId: number;
  displayName: string;
  username: string | null;
  position: number;
  joinedAt: string;
  isMe: boolean;
  isOwner: boolean;
};
export type InviteDto = { code: string; miniAppLink: string | null; botLink: string | null };
export type GroupDto = {
  id: string;
  name: string;
  createdAt: string;
  ownerId: number;
  isOwner: boolean;
  members: MemberDto[];
  invite: InviteDto;
};
export type InvitePreviewDto = {
  groupName: string;
  memberNames: string[];
  memberCount: number;
  alreadyMember: boolean;
  groupId: string | null;
};
export type JoinResultDto = { joined: boolean; group: GroupDto };

const group = (id: string): string => `/movienight/groups/${encodeURIComponent(id)}`;
const invite = (code: string): string => `/movienight/invites/${encodeURIComponent(code)}`;

export const movieNightApi = {
  listGroups: () => api.get<GroupSummaryDto[]>('/movienight/groups'),
  createGroup: (req: CreateGroupRequest) => api.post<GroupDto>('/movienight/groups', req),
  getGroup: (id: string) => api.get<GroupDto>(group(id)),
  renameGroup: (id: string, req: RenameGroupRequest) => api.put<GroupDto>(`${group(id)}/name`, req),
  regenerateInvite: (id: string) => api.post<InviteDto>(`${group(id)}/invite`),
  removeMember: (id: string, userId: number) => api.del<void>(`${group(id)}/members/${userId}`),
  leaveGroup: (id: string) => api.post<void>(`${group(id)}/leave`),
  getInvite: (code: string) => api.get<InvitePreviewDto>(invite(code)),
  joinByInvite: (code: string) => api.post<JoinResultDto>(`${invite(code)}/join`),
};

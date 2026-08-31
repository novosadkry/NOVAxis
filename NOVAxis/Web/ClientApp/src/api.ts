// Typed client for the bot's api. Mirrors NOVAxis.Web.Contracts - snowflakes
// travel as strings, positions and durations in milliseconds.

export interface TrackDto {
  title: string
  author: string
  uri: string | null
  artworkUri: string | null
  durationMs: number
  isLiveStream: boolean
  sourceName: string | null
}

export interface WebUserDto {
  id: string
  name: string
  avatarUrl: string | null
}

export interface QueueItemDto {
  requestId: string
  track: TrackDto
  requestedBy: WebUserDto | null
}

export interface VoiceChannelDto {
  id: string
  name: string
}

export interface PlayerStateDto {
  guildId: string
  connected: boolean
  state: 'Destroyed' | 'NotPlaying' | 'Playing' | 'Paused'
  isPaused: boolean
  volume: number
  repeatMode: 'None' | 'Track' | 'Queue'
  positionMs: number
  sampledAt: number
  voiceChannel: VoiceChannelDto | null
  current: QueueItemDto | null
  queue: QueueItemDto[]
}

export interface GuildDto {
  id: string
  name: string
  iconUrl: string | null
  connected: boolean
}

export interface PlayResponse {
  enqueued: number
  track: TrackDto | null
  playlistName: string | null
}

export interface DownloadFormatDto {
  id: string
  kind: 'Video' | 'Audio'
  label: string
  extension: string
  sizeBytes: number | null
  withinLimit: boolean
}

export interface DownloadProbeDto {
  url: string
  title: string
  thumbnailUrl: string | null
  durationMs: number
  isLiveStream: boolean
  formats: DownloadFormatDto[]
}

export interface DownloadQuotaDto {
  limit: number
  remaining: number
  resetsAt: number | null
}

export type DownloadState = 'Pending' | 'Running' | 'Ready' | 'Failed' | 'Revoked' | 'Expired'

export interface DownloadDto {
  id: string
  state: DownloadState
  kind: 'Video' | 'Audio'
  title: string
  sourceUrl: string
  formatLabel: string
  fileName: string | null
  sizeBytes: number | null
  receivedBytes: number
  progress: number | null
  createdAt: number
  expiresAt: number
  /** The server's clock when this snapshot was taken - the countdown anchors to it. */
  sampledAt: number
  fileUrl: string | null
  error: string | null
}

export interface DownloadOverviewDto {
  active: DownloadDto | null
  quota: DownloadQuotaDto
}

export class ApiError extends Error {
  readonly code: string
  readonly status: number

  constructor(status: number, code: string, message: string) {
    super(message)
    this.status = status
    this.code = code
  }
}

export function isAbortError(error: unknown): boolean {
  return error instanceof Error && error.name === 'AbortError'
}

async function request<T>(path: string, init?: RequestInit): Promise<T> {
  const response = await fetch(path, {
    credentials: 'same-origin',
    headers: init?.body ? { 'Content-Type': 'application/json' } : undefined,
    ...init,
  })

  if (response.status === 204) return undefined as T

  const body = await response.json().catch(() => null)

  if (!response.ok) {
    throw new ApiError(
      response.status,
      body?.code ?? 'unknown',
      body?.message ?? 'Nastala neznámá chyba',
    )
  }

  return body as T
}

function post<T>(path: string, body?: unknown): Promise<T> {
  return request<T>(path, {
    method: 'POST',
    body: body === undefined ? '{}' : JSON.stringify(body),
  })
}

export const api = {
  me: () => request<WebUserDto>('/api/auth/me'),
  logout: () => post<void>('/api/auth/logout'),

  guilds: () => request<GuildDto[]>('/api/guilds'),
  state: (guildId: string) => request<PlayerStateDto>(`/api/guilds/${guildId}/state`),

  search: (guildId: string, query: string, signal?: AbortSignal, limit = 8) =>
    request<TrackDto[]>(
      `/api/guilds/${guildId}/search?q=${encodeURIComponent(query)}&limit=${limit}`,
      { signal },
    ),

  play: (guildId: string, query: string) =>
    post<PlayResponse>(`/api/guilds/${guildId}/play`, { query }),

  pause: (guildId: string) => post<void>(`/api/guilds/${guildId}/pause`),
  resume: (guildId: string) => post<void>(`/api/guilds/${guildId}/resume`),
  stop: (guildId: string) => post<void>(`/api/guilds/${guildId}/stop`),
  skip: (guildId: string) => post<void>(`/api/guilds/${guildId}/skip`, { count: 1 }),
  disconnect: (guildId: string) => post<void>(`/api/guilds/${guildId}/disconnect`),

  seek: (guildId: string, positionMs: number) =>
    post<void>(`/api/guilds/${guildId}/seek`, { positionMs }),

  volume: (guildId: string, percent: number) =>
    post<void>(`/api/guilds/${guildId}/volume`, { percent }),

  repeat: (guildId: string, mode: PlayerStateDto['repeatMode']) =>
    post<void>(`/api/guilds/${guildId}/repeat`, { mode }),

  clearQueue: (guildId: string) =>
    request<void>(`/api/guilds/${guildId}/queue`, { method: 'DELETE' }),

  removeItem: (guildId: string, requestId: string) =>
    request<void>(`/api/guilds/${guildId}/queue/${requestId}`, { method: 'DELETE' }),

  moveItem: (guildId: string, requestId: string, toIndex: number) =>
    post<void>(`/api/guilds/${guildId}/queue/${requestId}/move`, { toIndex }),

  downloads: () => request<DownloadOverviewDto>('/api/downloads'),

  probeDownload: (url: string, signal?: AbortSignal) =>
    request<DownloadProbeDto>(`/api/downloads/probe?url=${encodeURIComponent(url)}`, { signal }),

  startDownload: (url: string, kind: DownloadDto['kind'], formatId: string) =>
    post<DownloadDto>('/api/downloads', { url, kind, formatId }),

  revokeDownload: (id: string) =>
    request<void>(`/api/downloads/${id}`, { method: 'DELETE' }),
}

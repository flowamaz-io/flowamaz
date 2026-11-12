import { getCurrentInstance, onUnmounted, ref, type Ref } from 'vue';
import { BASE_URL, refreshAccessToken } from '@/services/api.service';

export type WsConnectionState = 'connecting' | 'live' | 'reconnecting' | 'lost' | 'closed';

/** Normalised status frame pushed by the backend (snake_case on the wire). */
export interface InstanceStatusFrame {
  eventType: string;
  status: string;
  nodeId: string | null;
  timestamp: string;
}

const MAX_RETRIES = 5;
const MAX_BACKOFF_MS = 30_000;

export interface InstanceWebSocket {
  connectionState: Ref<WsConnectionState>;
  lastStatus: Ref<string | null>;
  lastNodeId: Ref<string | null>;
  connect: () => void;
  disconnect: () => void;
}

/**
 * Live instance-status WebSocket. Pushes status changes into reactive refs and (optionally) an
 * `onFrame` callback. Reconnects with exponential backoff (capped at 30s, max 5 attempts) then
 * settles on `lost`. The access token is read lazily via `getToken` (passed in the query string —
 * browsers can't set Authorization headers on a WebSocket).
 */
export function useInstanceWebSocket(
  workspaceId: string,
  instanceId: string,
  getToken: () => string | null,
  onFrame?: (frame: InstanceStatusFrame) => void,
): InstanceWebSocket {
  const connectionState = ref<WsConnectionState>('connecting');
  const lastStatus = ref<string | null>(null);
  const lastNodeId = ref<string | null>(null);

  let socket: WebSocket | null = null;
  let retries = 0;
  let reconnectTimer: ReturnType<typeof setTimeout> | null = null;
  let manualClose = false;

  function buildUrl(): string {
    const token = getToken() ?? '';
    const wsBase = BASE_URL.replace(/^http/i, 'ws');
    return `${wsBase}/ws/v1/workspaces/${workspaceId}/instances/${instanceId}?token=${encodeURIComponent(token)}`;
  }

  function connect(): void {
    manualClose = false;
    connectionState.value = retries === 0 ? 'connecting' : 'reconnecting';
    socket = new WebSocket(buildUrl());
    let opened = false;

    socket.onopen = (): void => {
      opened = true;
      retries = 0;
      connectionState.value = 'live';
    };

    socket.onmessage = (event: MessageEvent): void => {
      try {
        const raw = JSON.parse(event.data as string) as {
          event_type?: string;
          status?: string;
          node_id?: string | null;
          timestamp?: string;
        };
        const frame: InstanceStatusFrame = {
          eventType: raw.event_type ?? 'StatusChanged',
          status: raw.status ?? 'Unknown',
          nodeId: raw.node_id ?? null,
          timestamp: raw.timestamp ?? new Date().toISOString(),
        };
        lastStatus.value = frame.status;
        lastNodeId.value = frame.nodeId;
        onFrame?.(frame);
      } catch {
        // Ignore malformed frames — the next poll will resync.
      }
    };

    socket.onclose = (event?: CloseEvent): void => {
      if (manualClose) return;
      // 4401: the session changed (e.g. a new role); a refused handshake may be an out-of-date token too.
      // Get a fresh token before trying again, so the retry doesn't fail the same way.
      scheduleReconnect(event?.code === 4401 || !opened);
    };

    socket.onerror = (): void => {
      socket?.close();
    };
  }

  function scheduleReconnect(refreshFirst = false): void {
    if (retries >= MAX_RETRIES) {
      connectionState.value = 'lost';
      return;
    }
    retries += 1;
    connectionState.value = 'reconnecting';
    const delay = Math.min(2 ** retries * 1000, MAX_BACKOFF_MS);
    // The backoff runs while the token is refreshed, so the retry still happens after `delay`.
    const tokenReady = refreshFirst ? refreshAccessToken().catch(() => null) : Promise.resolve(null);
    reconnectTimer = setTimeout(() => {
      void tokenReady.then(() => {
        if (!manualClose) connect();
      });
    }, delay);
  }

  function disconnect(): void {
    manualClose = true;
    if (reconnectTimer) {
      clearTimeout(reconnectTimer);
      reconnectTimer = null;
    }
    connectionState.value = 'closed';
    socket?.close();
    socket = null;
  }

  if (getCurrentInstance()) onUnmounted(disconnect);

  return { connectionState, lastStatus, lastNodeId, connect, disconnect };
}

import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { useInstanceWebSocket, type InstanceStatusFrame } from '@/composables/useInstanceWebSocket';

const refresh = vi.fn(async () => 'fresh');
vi.mock('@/services/api.service', () => ({ BASE_URL: 'http://localhost', refreshAccessToken: () => refresh() }));

class FakeWebSocket {
  static instances: FakeWebSocket[] = [];
  onopen: (() => void) | null = null;
  onmessage: ((ev: { data: string }) => void) | null = null;
  onclose: ((ev?: { code: number }) => void) | null = null;
  onerror: (() => void) | null = null;
  url: string;
  closed = false;

  constructor(url: string) {
    this.url = url;
    FakeWebSocket.instances.push(this);
  }

  close(): void {
    this.closed = true;
  }

  static latest(): FakeWebSocket {
    return FakeWebSocket.instances[FakeWebSocket.instances.length - 1]!;
  }
}

beforeEach(() => {
  FakeWebSocket.instances = [];
  refresh.mockClear();
  vi.stubGlobal('WebSocket', FakeWebSocket as unknown as typeof WebSocket);
});

afterEach(() => {
  vi.unstubAllGlobals();
  vi.useRealTimers();
});

describe('useInstanceWebSocket', () => {
  it('connects, goes live on open, and pushes parsed frames', () => {
    const frames: InstanceStatusFrame[] = [];
    const ws = useInstanceWebSocket('w1', 'i1', () => 'tok', (f) => frames.push(f));

    ws.connect();
    expect(ws.connectionState.value).toBe('connecting');
    expect(FakeWebSocket.latest().url).toContain('/ws/v1/workspaces/w1/instances/i1?token=tok');

    FakeWebSocket.latest().onopen?.();
    expect(ws.connectionState.value).toBe('live');

    FakeWebSocket.latest().onmessage?.({
      data: JSON.stringify({ event_type: 'StatusChanged', status: 'Running', node_id: 'call', timestamp: 't' }),
    });

    expect(ws.lastStatus.value).toBe('Running');
    expect(ws.lastNodeId.value).toBe('call');
    expect(frames).toHaveLength(1);
    expect(frames[0]!.status).toBe('Running');
  });

  it('reconnects with backoff on unexpected close', async () => {
    vi.useFakeTimers();
    const ws = useInstanceWebSocket('w1', 'i1', () => 'tok');
    ws.connect();
    FakeWebSocket.latest().onopen?.();

    FakeWebSocket.latest().onclose?.({ code: 1006 });
    expect(ws.connectionState.value).toBe('reconnecting');

    await vi.advanceTimersByTimeAsync(2000); // 2^1 * 1000ms
    expect(FakeWebSocket.instances.length).toBe(2);
    expect(refresh).not.toHaveBeenCalled();
  });

  it('gets a fresh token before reconnecting when the server ends the session (4401)', async () => {
    vi.useFakeTimers();
    let token = 'old';
    refresh.mockImplementationOnce(async () => {
      token = 'fresh';
      return token;
    });
    const ws = useInstanceWebSocket('w1', 'i1', () => token);
    ws.connect();
    FakeWebSocket.latest().onopen?.();

    FakeWebSocket.latest().onclose?.({ code: 4401 });
    await vi.advanceTimersByTimeAsync(2000);

    expect(refresh).toHaveBeenCalledOnce();
    expect(FakeWebSocket.latest().url).toContain('token=fresh');
  });

  it('gives up after the max retries and reports lost', async () => {
    vi.useFakeTimers();
    const ws = useInstanceWebSocket('w1', 'i1', () => 'tok');
    ws.connect();

    for (let i = 0; i < 6; i++) {
      FakeWebSocket.latest().onclose?.();
      await vi.advanceTimersByTimeAsync(40_000);
    }

    expect(ws.connectionState.value).toBe('lost');
  });

  it('disconnect closes the socket and stops reconnecting', () => {
    const ws = useInstanceWebSocket('w1', 'i1', () => 'tok');
    ws.connect();
    const socket = FakeWebSocket.latest();

    ws.disconnect();
    expect(ws.connectionState.value).toBe('closed');
    expect(socket.closed).toBe(true);

    // A close arriving after manual disconnect must NOT trigger a reconnect.
    socket.onclose?.();
    expect(FakeWebSocket.instances.length).toBe(1);
  });
});

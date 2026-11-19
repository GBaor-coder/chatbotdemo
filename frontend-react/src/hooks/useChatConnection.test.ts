import { act, renderHook } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { useChatConnection } from "./useChatConnection";
import {
  HubConnectionBuilder,
  LogLevel,
  __getSubscribeHandlers,
  __getLastDispose,
  __resetSignalr,
} from "@microsoft/signalr";

vi.mock("@microsoft/signalr", () => {
  let subscribeHandlers: any = null;
  let lastDispose: any = null;

  const connection = {
    start: vi.fn().mockResolvedValue(undefined),
    stream: vi.fn(() => ({
      subscribe: vi.fn((handlers: any) => {
        subscribeHandlers = handlers;
        lastDispose = { dispose: vi.fn() };
        return lastDispose;
      }),
    })),
    on: vi.fn(),
    off: vi.fn(),
    dispose: vi.fn(),
    state: "Connected",
  };

  return {
    HubConnectionBuilder: vi.fn(() => ({
      withUrl: vi.fn().mockReturnThis(),
      accessTokenFactory: vi.fn().mockReturnThis(),
      withAutomaticReconnect: vi.fn().mockReturnThis(),
      configureLogging: vi.fn().mockReturnThis(),
      build: vi.fn(() => connection),
    })),
    LogLevel: { Information: 1 },
    __getSubscribeHandlers: () => subscribeHandlers,
    __getLastDispose: () => lastDispose,
    __resetSignalr: () => {
      subscribeHandlers = null;
      lastDispose = null;
      vi.clearAllMocks();
    },
  };
});

beforeEach(() => {
  __resetSignalr();
});

async function sendMessage(result: any, text: string) {
  await act(async () => {
    result.current.sendMessage(text);
  });
  return __getSubscribeHandlers();
}

describe("useChatConnection", () => {
  it("nhận các sự kiện theo thứ tự token → citations → done", async () => {
    const { result } = renderHook(() => useChatConnection());

    const handlers = await sendMessage(result, "điểm chuẩn CNTT");

    act(() => {
      handlers.next({ type: "token", content: "Điểm", sessionId: "s1" });
      handlers.next({ type: "token", content: " chuẩn", sessionId: "s1" });
      handlers.next({
        type: "citations",
        sources: [{ id: "c1", title: "Nguồn 1", source: "s" }],
        sessionId: "s1",
      });
      handlers.next({ type: "done", sessionId: "s1", done: true });
      handlers.complete();
    });

    expect(result.current.messages.filter((m) => m.role === "assistant").map((m) => m.content).join("")).toBe("Điểm chuẩn");
    expect(result.current.sessionId).toBe("s1");
    expect(result.current.isStreaming).toBe(false);
  });

  it("nút Dừng gọi dispose() của subscription", async () => {
    const { result } = renderHook(() => useChatConnection());

    await sendMessage(result, "x");
    act(() => {
      __getSubscribeHandlers().next({ type: "token", content: "x", sessionId: "s1" });
    });

    const dispose = __getLastDispose();
    expect(dispose).not.toBeNull();

    act(() => {
      result.current.stopStream();
    });

    expect(dispose.dispose).toHaveBeenCalled();
    expect(result.current.isStreaming).toBe(false);
  });

  it("lỗi/đứt kết nối đặt isStreaming=false và cho phép gửi lại", async () => {
    const { result } = renderHook(() => useChatConnection());

    await sendMessage(result, "x");
    act(() => {
      __getSubscribeHandlers().next({ type: "token", content: "x", sessionId: "s1" });
    });

    act(() => {
      __getSubscribeHandlers().error(new Error("mất kết nối"));
    });

    expect(result.current.isStreaming).toBe(false);
    expect(result.current.streamError).toBe("Không thể kết nối đến máy chủ.");

    // gửi lại sau lỗi
    const handlers2 = await sendMessage(result, "y");
    expect(handlers2).not.toBeNull();
    act(() => {
      handlers2.next({ type: "done", sessionId: "s2", done: true });
      handlers2.complete();
    });

    expect(result.current.isStreaming).toBe(false);
    expect(result.current.streamError).toBeUndefined();
  });
});

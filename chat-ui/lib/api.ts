import { Message } from "@/types/chat";

const API_BASE = process.env.NEXT_PUBLIC_API_URL ?? "http://localhost:52238";

export interface ApiResponse {
  reply: string;
  tokensUsed: number;
}

/** Send full conversation history and get next reply */
export async function sendConversation(
  messages: Message[],
  signal?: AbortSignal
): Promise<ApiResponse> {
  const body = {
    messages: messages.map((m) => ({ role: m.role, content: m.content })),
  };

  const res = await fetch(`${API_BASE}/api/chat/conversation`, {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify(body),
    signal,
  });

  if (!res.ok) {
    const err = await res.json().catch(() => ({ error: "Unknown error" }));
    throw new Error(err.error ?? `HTTP ${res.status}`);
  }

  return res.json();
}

/**
 * Stream a single message via SSE.
 * Calls onChunk for each text piece, returns when [DONE] is received.
 */
export async function streamMessage(
  message: string,
  onChunk: (text: string) => void,
  signal?: AbortSignal
): Promise<void> {
  const res = await fetch(`${API_BASE}/api/chatstream`, {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ message }),
    signal,
  });

  if (!res.ok) {
    const err = await res.json().catch(() => ({ error: "Unknown error" }));
    throw new Error(err.error ?? `HTTP ${res.status}`);
  }

  const reader = res.body?.getReader();
  if (!reader) throw new Error("No response body");

  const decoder = new TextDecoder();
  let buffer = "";

  while (true) {
    const { done, value } = await reader.read();
    if (done) break;

    buffer += decoder.decode(value, { stream: true });

    // SSE lines come as "data: {...}\n\n" — split on double newline
    const parts = buffer.split("\n\n");
    buffer = parts.pop() ?? ""; // keep incomplete last chunk

    for (const part of parts) {
      const line = part.trim();
      if (!line.startsWith("data:")) continue;

      const raw = line.slice(5).trim();
      if (raw === "[DONE]") return;

      try {
        const parsed = JSON.parse(raw);
        if (parsed.error) throw new Error(parsed.error);
        if (parsed.text) onChunk(parsed.text);
      } catch {
        // skip malformed chunks
      }
    }
  }
}

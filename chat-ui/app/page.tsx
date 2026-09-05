"use client";

import { useEffect, useRef, useState } from "react";
import { streamMessage } from "@/lib/api";
import { Message } from "@/types/chat";
import Link from "next/link";

const uid = () => crypto.randomUUID();

// Render text with basic markdown: **bold**, `code`, newlines
function MessageText({ text }: { text: string }) {
  const lines = text.split("\n");
  return (
    <div className="space-y-1">
      {lines.map((line, i) => {
        if (line.startsWith("```") || line.endsWith("```")) {
          return <div key={i} className="font-mono text-xs text-emerald-400">{line}</div>;
        }
        const parts = line.split(/(\*\*[^*]+\*\*|`[^`]+`)/g);
        return (
          <p key={i} className={line === "" ? "h-2" : ""}>
            {parts.map((part, j) => {
              if (part.startsWith("**") && part.endsWith("**"))
                return <strong key={j}>{part.slice(2, -2)}</strong>;
              if (part.startsWith("`") && part.endsWith("`"))
                return (
                  <code key={j} className="bg-zinc-700 px-1 rounded text-xs font-mono text-emerald-300">
                    {part.slice(1, -1)}
                  </code>
                );
              return <span key={j}>{part}</span>;
            })}
          </p>
        );
      })}
    </div>
  );
}

// Blinking cursor shown while streaming
function Cursor() {
  return (
    <span className="inline-block w-[2px] h-[1em] bg-zinc-300 ml-0.5 align-middle animate-pulse" />
  );
}

export default function ChatPage() {
  const [messages, setMessages] = useState<Message[]>([]);
  const [input, setInput] = useState("");
  const [loading, setLoading] = useState(false);
  const [streamingId, setStreamingId] = useState<string | null>(null);
  const bottomRef = useRef<HTMLDivElement>(null);
  const textareaRef = useRef<HTMLTextAreaElement>(null);
  const abortRef = useRef<AbortController | null>(null);

  // Auto-scroll to latest message
  useEffect(() => {
    bottomRef.current?.scrollIntoView({ behavior: "smooth" });
  }, [messages, loading]);

  // Auto-resize textarea
  useEffect(() => {
    const el = textareaRef.current;
    if (!el) return;
    el.style.height = "auto";
    el.style.height = `${Math.min(el.scrollHeight, 160)}px`;
  }, [input]);

  async function handleSend() {
    const text = input.trim();
    if (!text || loading) return;

    const userMsg: Message = { id: uid(), role: "user", content: text };
    const aiMsgId = uid();
    const aiMsg: Message = { id: aiMsgId, role: "assistant", content: "" };

    setMessages((prev) => [...prev, userMsg, aiMsg]);
    setInput("");
    setLoading(true);
    setStreamingId(aiMsgId);

    abortRef.current = new AbortController();

    try {
      await streamMessage(
        text,
        (chunk) => {
          setMessages((prev) =>
            prev.map((m) =>
              m.id === aiMsgId ? { ...m, content: m.content + chunk } : m
            )
          );
        },
        abortRef.current.signal
      );
    } catch (err: unknown) {
      if (err instanceof Error && err.name === "AbortError") {
        // mark as stopped but keep what was streamed
        setMessages((prev) =>
          prev.map((m) =>
            m.id === aiMsgId && m.content === ""
              ? { ...m, content: "Response stopped.", error: true }
              : m
          )
        );
        return;
      }
      const errMsg = err instanceof Error ? err.message : "Something went wrong.";
      setMessages((prev) =>
        prev.map((m) =>
          m.id === aiMsgId ? { ...m, content: errMsg, error: true } : m
        )
      );
    } finally {
      setLoading(false);
      setStreamingId(null);
      abortRef.current = null;
      setTimeout(() => textareaRef.current?.focus(), 50);
    }
  }

  function handleKeyDown(e: React.KeyboardEvent<HTMLTextAreaElement>) {
    if (e.key === "Enter" && !e.shiftKey) {
      e.preventDefault();
      handleSend();
    }
  }

  function handleStop() {
    abortRef.current?.abort();
  }

  function handleClear() {
    if (loading) handleStop();
    setMessages([]);
  }

  return (
    <div className="flex h-full flex-col">
      {/* ── Header ── */}
      <header className="flex items-center justify-between border-b border-zinc-800 px-5 py-3 shrink-0">
        <div className="flex items-center gap-3">
          <div className="flex h-8 w-8 items-center justify-center rounded-lg bg-indigo-600 text-sm font-bold">
            AI
          </div>
          <div>
            <p className="text-sm font-semibold leading-none">Dev Assistant</p>
            <p className="text-xs text-zinc-400 mt-0.5">.NET · C# · Azure · Angular · SQL</p>
          </div>
        </div>
        <div className="flex items-center gap-3">
          {messages.length > 0 && (
            <button
              onClick={handleClear}
              className="text-xs text-zinc-400 hover:text-zinc-200 transition-colors"
            >
              Clear chat
            </button>
          )}
          <Link href="/rag" className="text-xs text-violet-400 hover:text-violet-300 transition-colors">
            Document Q&amp;A →
          </Link>
        </div>
      </header>

      {/* ── Messages ── */}
      <main className="flex-1 overflow-y-auto px-4 py-6 space-y-6">
        {messages.length === 0 && (
          <div className="flex h-full flex-col items-center justify-center gap-4 text-center text-zinc-500">
            <div className="text-4xl">💬</div>
            <p className="text-sm max-w-xs">
              Ask anything about .NET, C#, Azure, Angular or SQL Server.
            </p>
            <div className="flex flex-wrap justify-center gap-2 mt-2">
              {[
                "What is LINQ?",
                "Explain async/await in C#",
                "How does Entity Framework work?",
                "What is dependency injection?",
              ].map((s) => (
                <button
                  key={s}
                  onClick={() => { setInput(s); textareaRef.current?.focus(); }}
                  className="rounded-full border border-zinc-700 px-3 py-1 text-xs hover:border-indigo-500 hover:text-zinc-200 transition-colors"
                >
                  {s}
                </button>
              ))}
            </div>
          </div>
        )}

        {messages.map((msg) => (
          <div key={msg.id} className={`flex ${msg.role === "user" ? "justify-end" : "justify-start"}`}>
            <div className={`flex gap-3 max-w-[85%] ${msg.role === "user" ? "flex-row-reverse" : "flex-row"}`}>
              {/* Avatar */}
              <div className={`flex h-7 w-7 shrink-0 items-center justify-center rounded-full text-xs font-semibold mt-1
                ${msg.role === "user" ? "bg-indigo-600" : "bg-zinc-700"}`}>
                {msg.role === "user" ? "You" : "AI"}
              </div>

              {/* Bubble */}
              <div>
                <div className={`rounded-2xl px-4 py-3 text-sm leading-relaxed
                  ${msg.role === "user"
                    ? "bg-indigo-600 text-white rounded-tr-sm"
                    : msg.error
                    ? "bg-red-900/40 border border-red-700 text-red-300 rounded-tl-sm"
                    : "bg-zinc-800 text-zinc-100 rounded-tl-sm"}`}>
                  {/* Empty AI bubble while waiting for first chunk */}
                  {msg.role === "assistant" && msg.content === "" && streamingId === msg.id ? (
                    <span className="flex items-center gap-1 py-0.5">
                      {[0, 1, 2].map((i) => (
                        <span key={i} className="w-1.5 h-1.5 rounded-full bg-zinc-400 animate-bounce"
                          style={{ animationDelay: `${i * 0.15}s` }} />
                      ))}
                    </span>
                  ) : (
                    <>
                      <MessageText text={msg.content} />
                      {streamingId === msg.id && <Cursor />}
                    </>
                  )}
                </div>
              </div>
            </div>
          </div>
        ))}

        <div ref={bottomRef} />
      </main>

      {/* ── Input ── */}
      <footer className="border-t border-zinc-800 px-4 py-3 shrink-0">
        <div className="mx-auto max-w-3xl flex items-end gap-2">
          <textarea
            ref={textareaRef}
            value={input}
            onChange={(e) => setInput(e.target.value)}
            onKeyDown={handleKeyDown}
            placeholder="Ask something… (Enter to send, Shift+Enter for new line)"
            rows={1}
            disabled={loading}
            className="flex-1 resize-none rounded-xl border border-zinc-700 bg-zinc-900 px-4 py-3 text-sm
              text-zinc-100 placeholder-zinc-500 outline-none focus:border-indigo-500 transition-colors
              disabled:opacity-50 leading-relaxed"
          />
          {loading ? (
            <button
              onClick={handleStop}
              className="flex h-11 w-11 shrink-0 items-center justify-center rounded-xl bg-red-600 hover:bg-red-500 transition-colors"
              title="Stop"
            >
              <span className="h-3.5 w-3.5 rounded-sm bg-white" />
            </button>
          ) : (
            <button
              onClick={handleSend}
              disabled={!input.trim()}
              className="flex h-11 w-11 shrink-0 items-center justify-center rounded-xl bg-indigo-600
                hover:bg-indigo-500 disabled:opacity-40 disabled:cursor-not-allowed transition-colors"
              title="Send"
            >
              <svg className="h-4 w-4 text-white rotate-90" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M12 19l9 2-9-18-9 18 9-2zm0 0v-8" />
              </svg>
            </button>
          )}
        </div>
        <p className="mt-2 text-center text-[11px] text-zinc-600">
          Powered by Gemini 2.5 Flash · Streaming
        </p>
      </footer>
    </div>
  );
}

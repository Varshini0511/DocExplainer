"use client";

import { useEffect, useRef, useState } from "react";
import { askAgent, ingestChunks, readFileAsText, splitIntoChunks } from "@/lib/rag";
import Link from "next/link";

const uid = () => crypto.randomUUID();

type UploadStatus = "idle" | "reading" | "ingesting" | "done" | "error";

interface UploadedDoc {
  id: string;
  filename: string;
  chunks: number;
}

interface ChatMessage {
  id: string;
  role: "user" | "assistant";
  content: string;
  error?: boolean;
}

export default function RagPage() {
  const [uploadStatus, setUploadStatus] = useState<UploadStatus>("idle");
  const [uploadError, setUploadError] = useState("");
  const [uploadProgress, setUploadProgress] = useState("");
  const [docs, setDocs] = useState<UploadedDoc[]>([]);
  const [activeDoc, setActiveDoc] = useState<string | null>(null);
  const [dragging, setDragging] = useState(false);

  const [messages, setMessages] = useState<ChatMessage[]>([]);
  const [input, setInput] = useState("");
  const [loading, setLoading] = useState(false);

  const fileInputRef = useRef<HTMLInputElement>(null);
  const bottomRef = useRef<HTMLDivElement>(null);
  const textareaRef = useRef<HTMLTextAreaElement>(null);
  const abortRef = useRef<AbortController | null>(null);

  useEffect(() => {
    bottomRef.current?.scrollIntoView({ behavior: "smooth" });
  }, [messages, loading]);

  useEffect(() => {
    const el = textareaRef.current;
    if (!el) return;
    el.style.height = "auto";
    el.style.height = `${Math.min(el.scrollHeight, 140)}px`;
  }, [input]);

  // ── File upload ──────────────────────────────────────────
  async function processFile(file: File) {
    const allowed = ["txt", "md", "pdf"];
    const ext = file.name.split(".").pop()?.toLowerCase() ?? "";
    if (!allowed.includes(ext)) {
      setUploadError("Only .txt, .md and .pdf files are supported.");
      setUploadStatus("error");
      return;
    }

    setUploadStatus("reading");
    setUploadError("");
    setUploadProgress(`Reading ${file.name}…`);

    try {
      const text = await readFileAsText(file);
      const chunks = splitIntoChunks(text);

      setUploadStatus("ingesting");
      setUploadProgress(`Sending ${chunks.length} chunks to database…`);

      await ingestChunks(file.name, chunks);

      setDocs((prev) => [...prev, { id: uid(), filename: file.name, chunks: chunks.length }]);
      setActiveDoc(file.name);
      setMessages([]);     // clear chat when new doc is loaded
      setUploadStatus("done");
      setUploadProgress("");
    } catch (err) {
      setUploadError(err instanceof Error ? err.message : "Upload failed.");
      setUploadStatus("error");
      setUploadProgress("");
    }
  }

  function onFileChange(e: React.ChangeEvent<HTMLInputElement>) {
    const file = e.target.files?.[0];
    if (file) processFile(file);
    e.target.value = "";
  }

  function onDrop(e: React.DragEvent) {
    e.preventDefault();
    setDragging(false);
    const file = e.dataTransfer.files[0];
    if (file) processFile(file);
  }

  // ── Chat ─────────────────────────────────────────────────
  async function handleSend() {
    const text = input.trim();
    if (!text || loading) return;

    const userMsg: ChatMessage = { id: uid(), role: "user", content: text };
    setMessages((prev) => [...prev, userMsg]);
    setInput("");
    setLoading(true);

    abortRef.current = new AbortController();

    try {
      const data = await askAgent(text, abortRef.current.signal);
      setMessages((prev) => [
        ...prev,
        { id: uid(), role: "assistant", content: data.answer },
      ]);
    } catch (err) {
      if (err instanceof Error && err.name === "AbortError") return;
      setMessages((prev) => [
        ...prev,
        { id: uid(), role: "assistant", content: err instanceof Error ? err.message : "Error.", error: true },
      ]);
    } finally {
      setLoading(false);
      abortRef.current = null;
      setTimeout(() => textareaRef.current?.focus(), 50);
    }
  }

  function handleKeyDown(e: React.KeyboardEvent<HTMLTextAreaElement>) {
    if (e.key === "Enter" && !e.shiftKey) { e.preventDefault(); handleSend(); }
  }

  const hasDoc = docs.length > 0;

  return (
    <div className="flex h-full flex-col">
      {/* Header */}
      <header className="flex items-center justify-between border-b border-zinc-800 px-5 py-3 shrink-0">
        <div className="flex items-center gap-3">
          <div className="flex h-8 w-8 items-center justify-center rounded-lg bg-violet-600 text-sm font-bold">
            RAG
          </div>
          <div>
            <p className="text-sm font-semibold leading-none">Document Q&amp;A</p>
            <p className="text-xs text-zinc-400 mt-0.5">Upload a document · Ask questions from it</p>
          </div>
        </div>
        <Link href="/" className="text-xs text-zinc-400 hover:text-zinc-200 transition-colors">
          ← General Chat
        </Link>
      </header>

      <div className="flex flex-1 overflow-hidden">
        {/* ── Left panel: Upload ── */}
        <aside className="w-72 shrink-0 border-r border-zinc-800 flex flex-col p-4 gap-4 overflow-y-auto">
          <p className="text-xs font-semibold uppercase tracking-wider text-zinc-500">Document</p>

          {/* Drop zone */}
          <div
            onDragOver={(e) => { e.preventDefault(); setDragging(true); }}
            onDragLeave={() => setDragging(false)}
            onDrop={onDrop}
            onClick={() => fileInputRef.current?.click()}
            className={`flex flex-col items-center justify-center gap-2 rounded-xl border-2 border-dashed
              p-6 cursor-pointer transition-colors text-center
              ${dragging ? "border-violet-500 bg-violet-500/10" : "border-zinc-700 hover:border-violet-600 hover:bg-zinc-800/50"}`}
          >
            <div className="text-2xl">📄</div>
            <p className="text-xs text-zinc-400">
              Drag &amp; drop or <span className="text-violet-400">click to upload</span>
            </p>
            <p className="text-[11px] text-zinc-600">.txt · .md · .pdf</p>
            <input
              ref={fileInputRef}
              type="file"
              accept=".txt,.md,.pdf"
              className="hidden"
              onChange={onFileChange}
            />
          </div>

          {/* Progress / status */}
          {uploadStatus === "reading" || uploadStatus === "ingesting" ? (
            <div className="rounded-lg bg-zinc-800 px-3 py-2 text-xs text-zinc-300 flex items-center gap-2">
              <span className="h-3 w-3 rounded-full border-2 border-violet-400 border-t-transparent animate-spin" />
              {uploadProgress}
            </div>
          ) : null}

          {uploadStatus === "error" && (
            <div className="rounded-lg bg-red-900/30 border border-red-700 px-3 py-2 text-xs text-red-300">
              {uploadError}
            </div>
          )}

          {uploadStatus === "done" && (
            <div className="rounded-lg bg-emerald-900/30 border border-emerald-700 px-3 py-2 text-xs text-emerald-300">
              ✓ Document ingested successfully
            </div>
          )}

          {/* Uploaded docs list */}
          {docs.length > 0 && (
            <div className="flex flex-col gap-2">
              <p className="text-xs font-semibold uppercase tracking-wider text-zinc-500">Uploaded</p>
              {docs.map((doc) => (
                <button
                  key={doc.id}
                  onClick={() => { setActiveDoc(doc.filename); setMessages([]); }}
                  className={`w-full text-left rounded-lg px-3 py-2 border transition-colors
                    ${activeDoc === doc.filename
                      ? "bg-violet-900/40 border-violet-600"
                      : "bg-zinc-800 border-transparent hover:border-zinc-600"}`}
                >
                  <p className="text-xs font-medium text-zinc-200 truncate">{doc.filename}</p>
                  <p className="text-[11px] text-zinc-500 mt-0.5">{doc.chunks} chunks · {activeDoc === doc.filename ? "✓ active" : "click to select"}</p>
                </button>
              ))}
            </div>
          )}

          {!hasDoc && (
            <p className="text-[11px] text-zinc-600 text-center mt-2">
              Upload a document first, then ask questions about it below.
            </p>
          )}
        </aside>

        {/* ── Right panel: Chat ── */}
        <div className="flex flex-1 flex-col overflow-hidden">
          <main className="flex-1 overflow-y-auto px-4 py-6 space-y-6">
            {messages.length === 0 && (
              <div className="flex h-full flex-col items-center justify-center gap-3 text-center text-zinc-500">
                <div className="text-4xl">🔍</div>
                <p className="text-sm max-w-xs">
                  {hasDoc
                    ? "Document ready! Ask any question about it."
                    : "Upload a document on the left, then ask questions here."}
                </p>
              </div>
            )}

            {messages.map((msg) => (
              <div key={msg.id} className={`flex ${msg.role === "user" ? "justify-end" : "justify-start"}`}>
                <div className={`flex gap-3 max-w-[90%] ${msg.role === "user" ? "flex-row-reverse" : "flex-row"}`}>
                  <div className={`flex h-7 w-7 shrink-0 items-center justify-center rounded-full text-xs font-semibold mt-1
                    ${msg.role === "user" ? "bg-violet-600" : "bg-zinc-700"}`}>
                    {msg.role === "user" ? "You" : "AI"}
                  </div>

                  <div className="flex flex-col gap-2 min-w-0">
                    <div className={`rounded-2xl px-4 py-3 text-sm leading-relaxed whitespace-pre-wrap
                      ${msg.role === "user"
                        ? "bg-violet-600 text-white rounded-tr-sm"
                        : msg.error
                        ? "bg-red-900/40 border border-red-700 text-red-300 rounded-tl-sm"
                        : "bg-zinc-800 text-zinc-100 rounded-tl-sm"}`}>
                      {msg.content}
                    </div>

                  </div>
                </div>
              </div>
            ))}

            {loading && (
              <div className="flex justify-start">
                <div className="flex gap-3">
                  <div className="flex h-7 w-7 shrink-0 items-center justify-center rounded-full bg-zinc-700 text-xs mt-1">AI</div>
                  <div className="rounded-2xl rounded-tl-sm bg-zinc-800 px-4 py-3 flex items-center gap-1">
                    {[0,1,2].map((i) => (
                      <span key={i} className="w-1.5 h-1.5 rounded-full bg-zinc-400 animate-bounce"
                        style={{ animationDelay: `${i * 0.15}s` }} />
                    ))}
                  </div>
                </div>
              </div>
            )}
            <div ref={bottomRef} />
          </main>

          {/* Input */}
          <footer className="border-t border-zinc-800 px-4 py-3 shrink-0">
            <div className="flex items-end gap-2">
              <textarea
                ref={textareaRef}
                value={input}
                onChange={(e) => setInput(e.target.value)}
                onKeyDown={handleKeyDown}
                placeholder={hasDoc ? "Ask a question about the document…" : "Upload a document first…"}
                rows={1}
                disabled={loading || !hasDoc}
                className="flex-1 resize-none rounded-xl border border-zinc-700 bg-zinc-900 px-4 py-3 text-sm
                  text-zinc-100 placeholder-zinc-500 outline-none focus:border-violet-500 transition-colors
                  disabled:opacity-50 leading-relaxed"
              />
              <button
                onClick={handleSend}
                disabled={!input.trim() || loading || !hasDoc}
                className="flex h-11 w-11 shrink-0 items-center justify-center rounded-xl bg-violet-600
                  hover:bg-violet-500 disabled:opacity-40 disabled:cursor-not-allowed transition-colors"
              >
                <svg className="h-4 w-4 text-white rotate-90" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                  <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M12 19l9 2-9-18-9 18 9-2zm0 0v-8" />
                </svg>
              </button>
            </div>
            <p className="mt-2 text-center text-[11px] text-zinc-600">
              Answers are grounded in your document only
            </p>
          </footer>
        </div>
      </div>
    </div>
  );
}

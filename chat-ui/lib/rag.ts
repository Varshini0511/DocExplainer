const API_BASE = process.env.NEXT_PUBLIC_API_URL ?? "http://localhost:52238";

// ── Ingest ──────────────────────────────────────────────────
export async function ingestChunks(
  filename: string,
  chunks: string[]
): Promise<{ chunksStored: number }> {
  const res = await fetch(`${API_BASE}/api/rag/ingest`, {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ filename, chunks }),
  });
  if (!res.ok) {
    const err = await res.json().catch(() => ({ error: "Unknown error" }));
    throw new Error(err.error ?? `HTTP ${res.status}`);
  }
  return res.json();
}

// ── Ask via RAG controller (legacy) ─────────────────────────
export interface RagAnswer {
  question: string;
  answer: string;
  sources: string[];
  chunks: { text: string; filename: string; similarity: string }[];
}

export async function askRag(
  question: string,
  filename?: string,
  signal?: AbortSignal
): Promise<RagAnswer> {
  const res = await fetch(`${API_BASE}/api/rag/ask`, {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ question, filename }),
    signal,
  });
  if (!res.ok) {
    const err = await res.json().catch(() => ({ error: "Unknown error" }));
    throw new Error(err.error ?? `HTTP ${res.status}`);
  }
  return res.json();
}

// ── Ask via Agent (uses Groq — no quota issues) ──────────────
export interface AgentAnswer {
  question: string;
  answer: string;
}

export async function askAgent(
  question: string,
  signal?: AbortSignal
): Promise<AgentAnswer> {
  const res = await fetch(`${API_BASE}/api/agent/ask`, {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ question }),
    signal,
  });
  if (!res.ok) {
    const err = await res.json().catch(() => ({ error: "Unknown error" }));
    throw new Error(err.error ?? `HTTP ${res.status}`);
  }
  return res.json();
}

// ── Text chunking (runs in browser) ─────────────────────────
// Splits text into ~800 char chunks on paragraph/sentence boundaries
export function splitIntoChunks(text: string, maxChars = 800): string[] {
  const paragraphs = text.split(/\n{2,}/).map((p) => p.trim()).filter(Boolean);
  const chunks: string[] = [];
  let current = "";

  for (const para of paragraphs) {
    if (current.length + para.length + 2 <= maxChars) {
      current += (current ? "\n\n" : "") + para;
    } else {
      if (current) chunks.push(current);
      // Para itself too long → split on sentences
      if (para.length > maxChars) {
        const sentences = para.match(/[^.!?]+[.!?]+/g) ?? [para];
        current = "";
        for (const s of sentences) {
          if (current.length + s.length + 1 <= maxChars) {
            current += (current ? " " : "") + s;
          } else {
            if (current) chunks.push(current);
            current = s;
          }
        }
      } else {
        current = para;
      }
    }
  }
  if (current) chunks.push(current);
  return chunks;
}

// ── PDF text extraction ──────────────────────────────────────
export async function extractTextFromPdf(file: File): Promise<string> {
  const pdfjsLib = await import("pdfjs-dist");
  pdfjsLib.GlobalWorkerOptions.workerSrc = `https://cdnjs.cloudflare.com/ajax/libs/pdf.js/${pdfjsLib.version}/pdf.worker.min.mjs`;

  const arrayBuffer = await file.arrayBuffer();
  const pdf = await pdfjsLib.getDocument({ data: arrayBuffer }).promise;
  const pages: string[] = [];

  for (let i = 1; i <= pdf.numPages; i++) {
    const page = await pdf.getPage(i);
    const content = await page.getTextContent();
    const pageText = content.items
      .map((item: unknown) => ("str" in (item as object) ? (item as { str: string }).str : ""))
      .join(" ");
    pages.push(pageText);
  }
  return pages.join("\n\n");
}

// ── Read any file as text ────────────────────────────────────
export async function readFileAsText(file: File): Promise<string> {
  const ext = file.name.split(".").pop()?.toLowerCase();
  if (ext === "pdf") return extractTextFromPdf(file);
  return new Promise((resolve, reject) => {
    const reader = new FileReader();
    reader.onload = () => resolve(reader.result as string);
    reader.onerror = reject;
    reader.readAsText(file);
  });
}

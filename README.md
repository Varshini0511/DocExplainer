# DocExplainer — AI Document Q&A with ReAct Agent

A full-stack AI application that lets you upload documents and ask questions about them using Retrieval Augmented Generation (RAG) and a ReAct agent pattern.

---

## What It Does

- Upload any `.txt`, `.md`, or `.pdf` document
- Ask questions about the document in a chat interface
- AI searches the document semantically and answers accurately
- Agent reasons step by step before answering (ReAct pattern)
- Answers are grounded in your documents — not made up

---

## Live Demo

> Coming soon after deployment

---

## Tech Stack

| Layer | Technology | Purpose |
|---|---|---|
| Frontend | Next.js 14 + TypeScript | Chat UI, file upload |
| Backend | .NET 8 Web API (C#) | RAG pipeline, ReAct agent |
| Database | PostgreSQL + pgvector | Vector storage and search |
| Embeddings | Google Gemini (gemini-embedding-001) | Text → 768-dim vectors |
| LLM | Groq (Llama 3.3 70B) | Reasoning and answer generation |

---

## How It Works — End to End

### Phase 1 — Upload a Document

```
User uploads file (PDF / TXT / MD)
        ↓
Browser splits text into chunks (~800 chars each)
        ↓
Each chunk sent to backend POST /api/rag/ingest
        ↓
Backend sends each chunk to Gemini Embedding API
        ↓
Gemini returns 768 numbers representing the meaning
        ↓
Numbers stored in PostgreSQL (pgvector) alongside chunk text
```

### Phase 2 — Ask a Question

```
User types a question
        ↓
POST /api/agent/ask
        ↓
ReAct Agent Loop starts:
  ┌─────────────────────────────────────────┐
  │ Thought: what tool should I use?        │
  │ Action:  SearchDocuments[query]         │
  │                  ↓                      │
  │ C# embeds query → searches pgvector     │
  │ Returns top 3 most similar chunks       │
  │                  ↓                      │
  │ Observation: [relevant chunks]          │
  │                  ↓                      │
  │ Thought: I have enough info             │
  │ Final Answer: answer from chunks        │
  └─────────────────────────────────────────┘
        ↓
Answer displayed in chat UI
```

---

## Key Concepts Used

### RAG (Retrieval Augmented Generation)
Instead of relying on the AI's training data, the system:
1. **Retrieves** relevant chunks from your documents
2. **Augments** the prompt with those chunks
3. **Generates** an answer grounded in your content

### Embeddings
Text is converted into 768 numbers (a vector) that represent its meaning. Similar sentences produce similar vectors. This allows semantic search — finding meaning, not just keywords.

### Vector Search (pgvector)
Uses cosine distance to compare the question vector against all stored chunk vectors. The closest matches are the most relevant chunks.

```
cosine_distance = 1 - (A · B) / (|A| × |B|)
Distance 0.0 = identical meaning
Distance 1.0 = completely different
```

### ReAct Pattern (Reasoning + Acting)
The agent thinks step by step:
```
Thought → Action → Observation → Thought → Final Answer
```
This allows the agent to use multiple tools in sequence and handle complex multi-part questions.

### Available Tools
| Tool | When Used | What It Does |
|---|---|---|
| `GetChunkCount` | "How many documents?" | Runs SQL COUNT query |
| `SearchDocuments[query]` | Content questions | Embeds query → pgvector search |

---

## Project Structure

```
DocExplainer/
│
├── backend/                        .NET 8 Web API
│   ├── Controllers/
│   │   ├── AgentController.cs      POST /api/agent/ask
│   │   ├── RagController.cs        POST /api/rag/ingest, /api/rag/ask
│   │   ├── ChatController.cs       POST /api/chat/conversation
│   │   └── ChatStreamController.cs POST /api/chatstream (SSE)
│   │
│   ├── Services/
│   │   ├── AgentService.cs         ReAct loop, tool execution
│   │   ├── EmbeddingService.cs     Gemini embeddings, pgvector save/search
│   │   ├── GeminiService.cs        Gemini chat service
│   │   └── ClaudeService.cs        Streaming chat service
│   │
│   ├── Models/
│   │   └── ChatModels.cs           Request/response DTOs
│   │
│   ├── Program.cs                  Service registration, middleware
│   ├── DocExplainer.csproj         NuGet packages
│   └── appsettings.json            Configuration (no secrets)
│
├── chat-ui/                        Next.js 14 Frontend
│   ├── app/
│   │   ├── page.tsx                General streaming chat UI
│   │   └── rag/
│   │       └── page.tsx            Document Q&A UI
│   │
│   ├── lib/
│   │   ├── api.ts                  Chat API calls (streaming)
│   │   └── rag.ts                  RAG API calls, chunking, PDF parsing
│   │
│   └── types/
│       └── chat.ts                 TypeScript interfaces
│
└── README.md
```

---

## API Endpoints

### Agent
```
POST /api/agent/ask
Body: { "question": "What is the refund policy?" }
Returns: { "question": "...", "answer": "..." }
```

### RAG
```
POST /api/rag/ingest
Body: { "filename": "doc.txt", "chunks": ["text1", "text2"] }
Returns: { "chunksStored": 5 }

POST /api/rag/ask
Body: { "question": "...", "filename": "doc.txt" }
Returns: { "question": "...", "answer": "...", "sources": [], "chunks": [] }
```

### Chat
```
POST /api/chat/conversation
Body: { "messages": [{ "role": "user", "content": "..." }] }
Returns: { "response": "..." }

POST /api/chatstream
Body: { "message": "..." }
Returns: SSE stream — data: {"text":"..."}\n\n
```

---

## Database Schema

```sql
CREATE EXTENSION IF NOT EXISTS vector;

CREATE TABLE document_chunks (
    id          SERIAL PRIMARY KEY,
    filename    TEXT NOT NULL,
    chunk_index INTEGER NOT NULL,
    chunk_text  TEXT NOT NULL,
    embedding   vector(768)      -- 768-dim Gemini embedding
);

-- Index for fast similarity search
CREATE INDEX ON document_chunks
USING hnsw (embedding vector_cosine_ops);
```

---

## Local Setup

### Prerequisites
- [.NET 8 SDK](https://dotnet.microsoft.com/download)
- [Node.js 18+](https://nodejs.org)
- [Docker](https://docker.com) (for local PostgreSQL)
- Gemini API key — [get free key](https://aistudio.google.com/app/apikey)
- Groq API key — [get free key](https://console.groq.com)

### 1. Start PostgreSQL with pgvector

```bash
docker run -d \
  --name pgvector-db \
  -e POSTGRES_PASSWORD=postgres123 \
  -p 5433:5432 \
  pgvector/pgvector:pg16
```

### 2. Create the table

```bash
docker exec -it pgvector-db psql -U postgres -c "
CREATE EXTENSION IF NOT EXISTS vector;
CREATE TABLE document_chunks (
    id SERIAL PRIMARY KEY,
    filename TEXT NOT NULL,
    chunk_index INTEGER NOT NULL,
    chunk_text TEXT NOT NULL,
    embedding vector(768)
);"
```

### 3. Configure Backend

Create `backend/appsettings.Development.json`:
```json
{
  "Gemini": {
    "ApiKey": "YOUR_GEMINI_API_KEY",
    "MaxOutputTokens": 8192,
    "Temperature": 0.7
  },
  "Groq": {
    "ApiKey": "YOUR_GROQ_API_KEY",
    "Model": "llama-3.3-70b-versatile"
  },
  "ConnectionStrings": {
    "DefaultConnection": "Host=localhost;Port=5433;Database=postgres;Username=postgres;Password=postgres123"
  }
}
```

### 4. Run Backend

```bash
cd backend
dotnet run
# Runs on http://localhost:52238
# Swagger UI at http://localhost:52238/swagger
```

### 5. Run Frontend

```bash
cd chat-ui
npm install
npm run dev
# Runs on http://localhost:3000
```

### 6. Open the App

- Chat UI: http://localhost:3000
- Document Q&A: http://localhost:3000/rag
- Swagger API docs: http://localhost:52238/swagger

---

## Environment Variables

### Backend
| Variable | Description |
|---|---|
| `Gemini:ApiKey` | Google Gemini API key (for embeddings) |
| `Groq:ApiKey` | Groq API key (for LLM inference) |
| `Groq:Model` | Groq model name (default: llama-3.3-70b-versatile) |
| `ConnectionStrings:DefaultConnection` | PostgreSQL connection string |

### Frontend
| Variable | Description |
|---|---|
| `NEXT_PUBLIC_API_URL` | Backend URL (default: http://localhost:52238) |

---

## Deployment

### Live URLs
| Service | URL |
|---|---|
| Frontend | https://docexplainer-xxx.vercel.app (update with your Vercel URL) |
| Backend | https://docexplainer.onrender.com |
| Swagger | https://docexplainer.onrender.com/swagger |

### Free Tier Behavior
| Service | Platform | What Happens |
|---|---|---|
| Frontend | Vercel | Free forever, never expires |
| Backend | Render | Sleeps after 15 min inactivity — first request takes ~60s to wake up |
| Database | Supabase | Pauses after 1 week of inactivity — restore from dashboard |

---

## How to Redeploy (If Services Go Inactive)

### If Supabase paused
1. Go to [supabase.com](https://supabase.com) → your project
2. Click **Restore project** (shown on the dashboard)
3. Wait ~2 min for it to come back online
4. No data is lost — it just paused

### If Render service is down
1. Go to [render.com](https://render.com) → DocExplainer service
2. Click **Manual Deploy → Deploy latest commit**
3. Wait 3-5 min for build to complete

### Full Redeploy from Scratch

If you need to redeploy everything from zero:

#### 1. Supabase — Create Database
1. Go to [supabase.com](https://supabase.com) → New project (or use existing)
2. Go to **SQL Editor** → run:
```sql
CREATE SCHEMA IF NOT EXISTS docexplainer;
CREATE EXTENSION IF NOT EXISTS vector;

CREATE TABLE IF NOT EXISTS docexplainer.document_chunks (
    id          SERIAL PRIMARY KEY,
    filename    TEXT NOT NULL,
    chunk_index INTEGER NOT NULL,
    chunk_text  TEXT NOT NULL,
    embedding   vector(768)
);

CREATE INDEX IF NOT EXISTS document_chunks_embedding_idx
ON docexplainer.document_chunks USING hnsw (embedding vector_cosine_ops);
```
3. Click **Connect** (top right) → copy the connection string URI
4. Replace `[YOUR-PASSWORD]` with your DB password

#### 2. Render — Deploy Backend
1. Go to [render.com](https://render.com) → New → Web Service
2. Connect GitHub → select **DocExplainer** repo
3. Settings:
   - **Language**: Docker
   - **Root Directory**: `backend`
   - **Branch**: `master`
   - **Instance Type**: Free
4. Add these **Environment Variables**:

| Key | Value |
|---|---|
| `ASPNETCORE_ENVIRONMENT` | `Production` |
| `ConnectionStrings__DefaultConnection` | `Host=db.xxx.supabase.co;Port=5432;Database=postgres;Username=postgres;Password=YOUR_PASSWORD;Search Path=docexplainer;SSL Mode=Require;Trust Server Certificate=true` |
| `Gemini__ApiKey` | Your Gemini API key from [aistudio.google.com](https://aistudio.google.com/app/apikey) |
| `Groq__ApiKey` | Your Groq API key from [console.groq.com](https://console.groq.com) |
| `Groq__Model` | `llama-3.3-70b-versatile` |

5. Click **Deploy web service** — wait 3-5 min
6. Copy the Render URL (e.g. `https://docexplainer.onrender.com`)

#### 3. Vercel — Deploy Frontend
1. Go to [vercel.com](https://vercel.com) → New Project
2. Import **DocExplainer** from GitHub
3. Settings:
   - **Root Directory**: `chat-ui`
   - **Framework**: Next.js
4. Add **Environment Variable**:

| Key | Value |
|---|---|
| `NEXT_PUBLIC_API_URL` | Your Render URL (e.g. `https://docexplainer.onrender.com`) |

5. Click **Deploy** — done!

### API Keys (Free Tier)
| Key | Where to Get | Free Limit |
|---|---|---|
| Gemini API Key | [aistudio.google.com/app/apikey](https://aistudio.google.com/app/apikey) | 1500 embedding requests/day |
| Groq API Key | [console.groq.com](https://console.groq.com) | Very generous free tier |

---

## Packages Used

### Backend (NuGet)
| Package | Purpose |
|---|---|
| `Npgsql.EntityFrameworkCore.PostgreSQL` | PostgreSQL driver |
| `Pgvector.EntityFrameworkCore` | pgvector type support |
| `Mscc.GenerativeAI` | Gemini SDK |
| `Swashbuckle.AspNetCore` | Swagger UI |

### Frontend (npm)
| Package | Purpose |
|---|---|
| `next` | React framework |
| `pdfjs-dist` | PDF text extraction |
| `tailwindcss` | Styling |

---

## What I Learned Building This

- How RAG pipelines work end to end
- How embeddings represent meaning as numbers
- How cosine similarity finds semantically similar text
- How pgvector stores and searches vectors efficiently
- How the ReAct agent pattern works (Thought → Action → Observation)
- How to integrate multiple AI APIs (.NET backend)
- How system prompts influence LLM behaviour
- Data privacy considerations when sending data to LLMs

---

## Author

**Varshini** — built as a learning project to understand AI/RAG concepts
- GitHub: [github.com/Varshini0511](https://github.com/Varshini0511)

# MyFirstAI — Your First Anthropic API Project

## Setup in 5 minutes

### Step 1 — Get your API key
Go to https://console.anthropic.com
Sign up (free) → API Keys → Create Key → Copy it

### Step 2 — Create the project
```bash
dotnet new webapi -n MyFirstAI
cd MyFirstAI
dotnet add package Anthropic.SDK
```

### Step 3 — Copy the files
Copy these files into your project:
- Controllers/ChatController.cs
- Models/ChatModels.cs
- Services/ClaudeService.cs
- Program.cs (replace the existing one)

### Step 4 — Add your API key
Open appsettings.Development.json and replace:
```json
"ApiKey": "YOUR_API_KEY_HERE"
```
with your actual key from Step 1.

### Step 5 — Run it
```bash
dotnet run
```
Go to https://localhost:7000/swagger
You will see your two endpoints ready to test.

---

## Test it in Swagger

### Single message test
- Click POST /api/chat → Try it out
- Paste this in the body:
```json
{ "message": "What is RAG in simple terms?" }
```
- Click Execute
- You should see Claude's reply in the response

### Conversation test
- Click POST /api/chat/conversation → Try it out
- Paste this:
```json
{
  "messages": [
    { "role": "user",      "content": "My name is Kumar" },
    { "role": "assistant", "content": "Hello Kumar! How can I help?" },
    { "role": "user",      "content": "What is my name?" }
  ]
}
```
- Claude will say "Your name is Kumar" — proving it reads the history

---

## Project structure
```
MyFirstAI/
├── Controllers/
│   └── ChatController.cs   ← Your API endpoints (POST /api/chat)
├── Models/
│   └── ChatModels.cs       ← Request/response shapes
├── Services/
│   └── ClaudeService.cs    ← All Anthropic API logic lives here
├── Program.cs              ← Wires everything together
└── appsettings.Development.json ← Your API key goes here
```

## What each file does

| File | Purpose |
|------|---------|
| ChatController | Receives HTTP requests, validates input, returns JSON |
| ChatModels | Defines what JSON looks like coming in and going out |
| ClaudeService | Makes the actual Anthropic API call, handles errors |
| Program.cs | Registers services in DI container, sets up middleware |

## Understanding the System Prompt
In ClaudeService.cs, look for SystemPrompt.
This is Claude's instructions — change this to make Claude behave
differently for your application.

Examples:
- "You are a customer support agent for an e-commerce store"
- "You are a code reviewer who only comments on security issues"
- "You answer only questions about our HR policy documents"

## Next steps after this works
1. Add streaming (tokens appear word by word)
2. Store conversation history in SQL Server
3. Add pgvector for document search (RAG)
4. Deploy to Azure App Service

## Cost awareness
Each API call uses tokens. Check usage at:
https://console.anthropic.com/settings/usage

The tokensUsed field in the response tells you how many tokens each reply used.

# Notepad AI (AI Professor & Interactive Study Companion)

> A client-side AI study, concept-mapping, and active-recall companion built with **Blazor WebAssembly (.NET 10)**, **OpenRouter Free Cloud Models (Gemma 4)**, **Tailwind CSS**, and **SQLite WASM**.
>
> All document parsing (`pdf.js`), BM25 vector retrieval (`VectorService`), and relational persistence (`SQLite WASM`) execute locally inside the user's browser, paired with fast cloud LLM inference and automatic multi-model fallback routing.

---

## Key Highlights

- **AI Professor & Academic Study Coach Persona**: Prompts are pedagogically engineered to teach concepts using definitions, step-by-step mechanisms, Bloom's Taxonomy assessments, and exam takeaways strictly grounded in uploaded lecture notes.
- **Hierarchical 3-Level Concept Tree**: Automatically organizes documents into a clean top-down taxonomy (`MAIN TOPIC` → `KEY CONCEPT` → `SUBCONCEPT`) with cubic Bézier branches, auto-wrapping node cards, and dynamic relationship pills.
- **Fast Free Cloud Inference (OpenRouter)**: Powered by **`google/gemma-4-26b-a4b-it:free`** (and `google/gemma-4-31b-it:free`) with automatic server-side fallback routing across free cloud models (`nvidia/nemotron-3.5-super-vl:free`, `baidu/ernie-5.5-flash-preview:free`, `particle-ai/particle-1:free`).
- **Local SQLite WASM Persistence**: Uploaded documents, generated concept trees, quizzes, flashcards, and chat histories are stored locally in the browser (`sql.js` + IndexedDB/LocalStorage) so previously generated study materials remain accessible offline.

---

## Features

### Hierarchical Concept Tree & Mind Map

- **3-Tier Tidy Tree Layout**: Non-overlapping top-down hierarchy separating the central root topic, foundational pillars, and detailed subconcepts.
- **Interactive SVG Canvas**: Smooth pan, cursor-anchored wheel zoom, view reset, and one-click **Rebuild Tree** regeneration.
- **Draggable Nodes**: Freely reposition concept cards with cubic Bézier edges and relationship badges following at 60 FPS.
- **Grounded Node Inspector**: Click any concept node to inspect its definition, mechanism, academic significance, and verbatim source passage citations with page numbers.

### Grounded AI Professor Chat

- **RAG-Powered Tutoring**: Ranks the most relevant document passages via in-browser BM25 / TF-IDF similarity (`VectorService`) before querying the LLM.
- **Structured Pedagogical Responses**: Every answer provides a **Core Takeaway**, a **Detailed Breakdown**, and a **Professor's Exam Tip / Self-Check**.
- **Inline Passage Citations**: Factual claims are linked to interactive `[Passage ID, p. page]` citation badges.

### Active Recall & Study Suite

- **Bloom's Taxonomy Quizzes**: Multiple-choice assessments testing conceptual understanding and cause-and-effect, complete with a **Professor's Solution Walkthrough** explaining why the correct choice is right and why distractors are common misconceptions.
- **Active-Recall Flashcards**: Targeted mechanism and comparison prompts on the front with concise, source-grounded answers on the back.
- **Essay Synthesizer & Rubric Grader**: Draft essays on document topics and receive constructive rubric-based scoring (100-point scale), strengths, and actionable revision steps.

### Modern, Distraction-Free UI

- **Design Inspiration**: Combines the structured information density of **Google NotebookLM**, modern AI chat interfaces, and **Gizmo**.
- **Dark & Light Modes**: Seamless switching between a charcoal dark palette and clean light theme.
- **Command Palette (`Ctrl + K`)**: Instant keyboard navigation across recent documents, library collections, and study views.

---

## Architecture & Tech Stack

```text
[User Browser (Blazor WebAssembly .NET 10)]
  ├── UI & State Orchestration      ── Pages/Home.razor, AppStateService
  ├── LLM Abstraction (ILlmService) ── OpenRouterService (Gemma 4 Free Cloud + Auto-Fallback)
  │                                    ├── GoogleAiStudioService (Gemini / Gemma Cloud API)
  │                                    └── WebLlmService (In-Browser WebGPU Engine)
  ├── SQLite (sql.js / WASM)        ── Local relational storage (Documents, Concept Trees, Quizzes, Cards)
  ├── PDF.js                        ── Client-side PDF text extraction & page chunking
  └── VectorService (BM25 / TF-IDF) ── In-browser passage tokenization & relevance ranking
```

- **Frontend Framework**: Blazor WebAssembly (.NET 10)
- **Styling**: Tailwind CSS v3
- **Cloud AI Engine**: [OpenRouter API](https://openrouter.ai/) (`google/gemma-4-26b-a4b-it:free`, `google/gemma-4-31b-it:free`, and automatic free fallback models)
- **Client Storage**: SQLite WASM via `sql.js` (persisted in browser storage)
- **Document Ingestion**: Client-side `pdf.js` for PDF text extraction and passage segmentation

---

## Getting Started

### Prerequisites

1. [.NET 10 SDK](https://dotnet.microsoft.com/download)
2. [Node.js](https://nodejs.org/) (for Tailwind CSS build toolchain)
3. A free [OpenRouter API Key](https://openrouter.ai/keys)

### 1. Clone the Repository

```bash
git clone https://github.com/cjalanano-dev/notepad-ai-cmsc203.git
cd BlazorApp1
```

### 2. Configure Your `.env` File

Copy `.env.example` to `.env` in the project root and add your OpenRouter API key:

```env
OPENROUTER_API_KEY=sk-or-v1-your_api_key_here
OPENROUTER_MODEL=google/gemma-4-26b-a4b-it:free
```

> **Note**: During build (`SyncEnvToAppSettings` target in `BlazorApp1.csproj`), `.env` values are automatically synced into `wwwroot/appsettings.Development.json` (which is git-ignored so your key is never committed).

### 3. Run the Application

```bash
dotnet run
```

1. Open your browser to `http://localhost:5189`.
2. Navigate to **AI Engine / Settings** in the sidebar to verify your OpenRouter connection or switch between free cloud models (`google/gemma-4-26b-a4b-it:free`, `google/gemma-4-31b-it:free`, `nvidia/nemotron-3.5-super-vl:free`, etc.).
3. Upload any PDF, text, or markdown file to generate hierarchical concept trees, flashcards, quizzes, and grounded professor chats!

---

## Scope & Limitations (Current Prototype)

- **Cloud LLM Inference Requirement**: While document parsing (`pdf.js`), passage ranking (`VectorService`), and database storage (`SQLite WASM`) operate 100% locally and offline in the browser, running large 26B–31B parameter models (`Gemma 4`) natively requires dedicated high-VRAM workstation GPUs. To ensure fast, high-accuracy academic generation on standard student laptops and mobile devices, new AI generations require an internet connection via OpenRouter's free cloud API, while previously generated concept maps, quizzes, and flashcards remain cached in local SQLite for offline review.

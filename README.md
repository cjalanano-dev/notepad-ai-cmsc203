# Notepad AI (In-Browser AI Study Companion)

> A 100% client-side, zero-server AI study and research companion built with **Blazor WebAssembly (.NET 10)**, **WebGPU (WebLLM)**, **Tailwind CSS**, and **SQLite WASM**.
> 
> Deployable directly as a static site on **GitHub Pages**—no backend server, no Docker, and no Ollama installation required. Everything runs privately inside the user's browser.

---

## Key Highlights

- **100% In-Browser Execution**: All document processing, vector search, database persistence, and LLM inference execute directly inside the browser using client hardware acceleration (WebGPU / WASM).
- **GitHub Pages Ready**: Static hosting compatible. Zero cloud API keys or ongoing server costs.
- **Ultra-Compact Model**: Powered by **`Qwen2.5-0.5B-Instruct`** (~350 MB download via WebLLM), cached permanently in the browser after initial load.
- **Complete Privacy**: Uploaded PDFs, notes, vector embeddings, and chats never leave the client device.

---

## Features

### Dynamic Concept Graph & Mind Map
- **Interactive Mind Map Canvas**: Pan, zoom, and explore auto-generated concept relationships.
- **Draggable Nodes**: Freely reposition concept nodes with dynamic connecting edges and relationship labels following in real-time.
- **Canvas Controls**: Zoom in/out, anchored wheel scrolling, and one-click view reset toolbar.
- **Verbatim Evidence Linking**: Click any node to open an inspection drawer showing grounded explanations along with exact source passage citations and page numbers.

### Grounded AI Reviewer Chat
- **RAG-Powered Chat**: Ask questions directly about your uploaded documents and notes.
- **Passage-Level Citations**: Every claim is verifiable with interactive source passage chips.
- **Conversational UI**: Clean chat drawer with modern pill input and frosted glass aesthetics.

### Active Recall & Study Suite
- **Interactive Quizzes**: Multiple-choice assessments generated directly from source materials with explanation breakdowns.
- **Concept Flashcards**: Flip cards for core definitions, algorithms, and key theorems.
- **Essay Synthesizer & Grader**: Draft essays on key topics and receive instant AI rubric grading and feedback.

### Modern, Distraction-Free UI
- **Design Inspiration**: Combines the structured information density of **Google NotebookLM**, the conversational polish of modern AI chats, and the sleek aesthetic of **Gizmo**.
- **Dark & Light Modes**: Seamless switching between a refined charcoal palette and clean light canvas.
- **Command Palette (`Ctrl + K`)**: Fast keyboard navigation across recent documents, library collections, and views.

---

## Architecture & Tech Stack

```
[User Browser (GitHub Pages / Static Host)]
  ├── Blazor WebAssembly (.NET 10)  ── UI, state orchestration, view routing
  ├── WebLLM + WebGPU               ── In-memory LLM inference (Qwen2.5-0.5B-Instruct)
  ├── SQLite (sql.js / WASM)        ── Local document storage & persistence
  ├── PDF.js                        ── Client-side PDF text extraction & chunking
  └── VectorService (BM25)          ── In-browser passage tokenization & ranking
```

- **Frontend Framework**: Blazor WebAssembly (.NET 10)
- **Styling**: Tailwind CSS v3
- **In-Browser AI**: [WebLLM](https://github.com/mlc-ai/web-llm) (`@mlc-ai/web-llm`) running on WebGPU
- **Client Storage**: SQLite WASM via `sql.js` (persisted in browser IndexedDB/LocalStorage)
- **Document Ingestion**: Client-side `pdf.js` for text extraction and chunk segmentation

---

## Getting Started

### Prerequisites

1. [.NET 10 SDK](https://dotnet.microsoft.com/download)
2. [Node.js](https://nodejs.org/) (for Tailwind CSS build toolchain)
3. A WebGPU-capable browser (Google Chrome, Microsoft Edge, Brave)

### 1. Clone the Repository

```bash
git clone https://github.com/carlosjames/BlazorApp1.git
cd BlazorApp1
```

### 2. Run the Application

```bash
dotnet watch run --urls "http://localhost:5189"
```

1. Open your browser to `http://localhost:5189`.
2. Navigate to **AI Engine** in the sidebar.
3. Click **Initialize / Preload In-Browser Model** to download and cache the ~350 MB `Qwen2.5-0.5B-Instruct` model.
4. Upload any PDF, text, or markdown file to generate concept maps, flashcards, quizzes, and grounded chats!

---

## Publishing to GitHub Pages

To publish as a static site for GitHub Pages:

```bash
dotnet publish -c Release -o dist
```

Deploy the contents of `dist/wwwroot` to your `gh-pages` branch. The app will run entirely static with zero backend dependencies.

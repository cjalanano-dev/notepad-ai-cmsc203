# Notepad AI (Offline Reviewer Prototype)

> An offline-first, local AI-powered study companion and research assistant built with **Blazor WebAssembly (.NET 10)**, **Tailwind CSS**, and **Ollama**. Turn documents into interactive concept mind maps, grounded flashcards, quizzes, and RAG-based AI chats—completely private and locally processed.

---

## Features

### Interactive Mind Map & Concept Graph
- **Dynamic Mind Map Canvas**: Pan, zoom, and explore auto-generated concept relationships.
- **Draggable Nodes**: Freely reposition concept nodes with connecting dynamic edges and relationship labels following in real-time.
- **Interactive Controls**: Zoom in/out, anchored wheel scrolling, and one-click view reset toolbar inspired by Google NotebookLM and Gizmo.
- **Verbatim Evidence Linking**: Click any node to open an inspection drawer showing grounded explanations along with exact source passage citations and page numbers.

### Grounded AI Reviewer Chat
- **RAG-Powered Chat**: Ask questions directly about your uploaded documents and notes.
- **Passage-Level Citations**: Every claim is verifiable with interactive source passage chips.
- **Conversational UI**: Clean, ChatGPT-inspired chat drawer with modern pill input and frosted glass aesthetics.

### Active Recall & Study Suite
- **Interactive Quizzes**: Multiple-choice assessments generated directly from source materials with explanation breakdowns.
- **Concept Flashcards**: Flip cards for core definitions, algorithms, and key theorems.
- **Essay Synthesizer & Grader**: Draft essays on key topics and receive instant AI rubric grading and feedback.

### Modern, Distraction-Free UI
- **Design Inspiration**: Combines the structured information density of **Google NotebookLM**, the conversational polish of **OpenAI ChatGPT**, and the smooth aesthetic of **Gizmo**.
- **Dark & Light Modes**: Seamless switching between a refined charcoal palette and clean light canvas.
- **Command Palette (`Ctrl + K`)**: Fast keyboard navigation across recent documents, library collections, and views.

---

## Architecture & Tech Stack

- **Frontend**: Blazor WebAssembly (.NET 10)
- **Styling**: Tailwind CSS v3 with customized typography & frosted glass utilities
- **Local AI Engine**: [Ollama](https://ollama.com/) (defaults to local models such as `gemma4:31b-cloud` or `llama3.2`)
- **Document Chunking & Vector Search**: In-memory vector embeddings and cosine similarity scoring for passage retrieval
- **Mind Map Visualization**: Custom SVG graph engine with hardware-accelerated pointer event dragging and scale transformations

---

## Getting Started

### Prerequisites

1. [.NET 10 SDK](https://dotnet.microsoft.com/download)
2. [Node.js](https://nodejs.org/) (for Tailwind CSS build toolchain)
3. [Ollama](https://ollama.com/) running locally (for offline AI inference)

### 1. Clone the Repository

```bash
git clone https://github.com/cjalanano-dev/offline-reviewer-prototype-2.git
cd offline-reviewer-prototype-2
```

### 2. Set Up Ollama

Start Ollama and pull your preferred model (configured by default in `Program.cs`):

```bash
ollama serve
ollama pull gemma4:31b-cloud
# Or pull llama3.2 / mistral and update Program.cs accordingly
```

> **Note:** If Ollama is unavailable or offline, the app provides grounded fallback study structures and sample interactive datasets for immediate testing.

### 3. Build & Run

Install Tailwind dependencies (handled automatically on `dotnet build`) and start the dev server:

```bash
# Build project
dotnet build

# Run Blazor WebAssembly app
dotnet run
```

Open your browser and navigate to:
```text
https://localhost:5001 or http://localhost:5000
```

---

## Keyboard Shortcuts

| Shortcut | Action |
| :--- | :--- |
| <kbd>Ctrl</kbd> + <kbd>K</kbd> | Open Command Palette / Search |
| <kbd>Ctrl</kbd> + <kbd>1</kbd> | Navigate to Recent Discoveries |
| <kbd>Ctrl</kbd> + <kbd>2</kbd> | Open Library Collections |
| <kbd>Mouse Drag</kbd> | Pan mind map canvas or drag concept nodes |
| <kbd>Scroll Wheel</kbd> | Smooth zoom centered on cursor position |

---

## Project Structure

```text
BlazorApp1/
├── Layout/
│   ├── MainLayout.razor        # Shell layout & Command Palette
│   └── NavMenu.razor           # Collapsible sidebar with study tool routes
├── Models/
│   ├── AppModels.cs            # Nodes, edges, documents, citations, quiz models
│   └── ExtractionModels.cs     # Structured JSON schemas for LLM extractions
├── Pages/
│   ├── Home.razor              # Main dashboard, mind map workspace, chat, and study modes
│   └── NotFound.razor          # 404 fallback page
├── Services/
│   ├── AppStateService.cs      # Central reactive UI state management
│   ├── ConceptService.cs       # Mind map node & edge generation
│   ├── DocumentService.cs      # File ingestion & passage splitting
│   ├── OllamaService.cs        # Local LLM API integration
│   ├── VectorService.cs        # Embeddings & local similarity search
│   └── StudyService.cs         # Quiz, flashcard, and essay grading routines
├── Styles/
│   └── input.css               # Tailwind source styles & canvas grid definitions
└── wwwroot/
    ├── concept-map.js          # High-performance 60fps pan/zoom/drag JS engine
    └── css/app.css             # Compiled Tailwind stylesheet
```

---



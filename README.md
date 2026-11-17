# Notepad AI (AI Professor & Interactive Study Companion)

> **CMSC 203 Final Project**  
> **Institution:** Laguna State Polytechnic University (LSPU) — Province of Laguna, College of Computer Studies  
> **Course, Year & Section:** BSCS 2A  
> **Proponents:**  
> - Alanano, Carlos James B.  
> - Banaag, Camille E.  
> - Coroza, Rosch Vielle C.  
> - Moredo, Christian Stephen D.  

---

## Project Overview

**Notepad AI** is an AI-powered academic reviewer, concept-mapping, and active-recall study companion built using **C#** and **Blazor WebAssembly (.NET 10)** with an embedded **SQLite WebAssembly (`sql.js`)** relational database. Inspired by Google NotebookLM and modern active-recall learning systems, Notepad AI transforms static lecture notes, syllabi, and textbooks (PDFs, Markdown, and plain text) into interactive, source-grounded learning workspaces.

Unlike generic conversational chatbots that often hallucinate facts outside the course syllabus, Notepad AI strictly grounds every output in the student's uploaded document passages using **Retrieval-Augmented Generation (RAG)** powered by client-side **TF-IDF Cosine Similarity** (`VectorService.cs`) and fast cloud LLM inference via **OpenRouter** (`google/gemma-4-26b-a4b-it:free`, `google/gemma-4-31b-it:free`, with automatic multi-model fallback).

All AI prompts are pedagogically engineered to embody an **AI Professor & Academic Study Coach** persona—teaching concepts using formal definitions, cause-and-effect breakdowns, Bloom's Taxonomy assessments, and exam takeaways strictly cited against uploaded notes.

---

## System Architecture & 5-Stage Pipeline

The application operates through a client-centric 5-stage pipeline:

```text
[User Browser (Blazor WebAssembly .NET 10)]
  │
  ├── 1. Application Initialization & Local DB Hydration
  │       └── Program.cs ──> SqliteStorageService ──> sqlite-storage.js (sql.js / IndexedDB)
  │
  ├── 2. Document Ingestion & Overlapping Passage Chunking
  │       └── Pages/Home.razor ──> pdf.js (Client Parser) ──> DocumentService (ChunkSize=1200, Overlap=150)
  │
  ├── 3. Radial Concept Graph Extraction & Polar Layout
  │       └── ConceptService ──> ILlmService (Structured JSON) ──> Deterministic Polar Layout (SVG)
  │
  ├── 4. Vector Retrieval (TF-IDF + Cosine Similarity) & Professor Chat
  │       └── ChatService ──> VectorService (Sublinear TF-IDF, Top-K Cosine) ──> Grounded Citations
  │
  └── 5. Active Recall Generation & Assessment Suite
          └── StudyService ──> Bloom's Taxonomy Quizzes, Interactive Flashcards, Rubric Essay Grader
```

1. **Application Initialization & Local Database Hydration**: On startup, `Program.cs` registers core services (`AppStateService`, `SqliteStorageService`, `DocumentService`, `VectorService`, `ConceptService`, `ChatService`, `StudyService`, `ILlmService`). `SqliteStorageService` invokes `sqlite-storage.js` to hydrate the persistent SQLite database from IndexedDB and ensures all 8 relational tables exist.
2. **Document Ingestion & Overlapping Passage Chunking**: Uploaded `.pdf`, `.txt`, and `.md` files are parsed client-side via Mozilla `pdf.js`. `DocumentService` splits text into semantic chunks (`ChunkSize = 1200`, `ChunkOverlap = 150`) to preserve context across boundaries and persists them in SQLite.
3. **Connected Radial Concept Graph Extraction & Polar Layout**: `ConceptService` samples representative document passages and prompts the LLM for structured concept and relationship extraction. It then runs a deterministic radial layout placing the main topic at `(450, 320)` with primary concepts and subconcepts arranged along polar orbits.
4. **Vector Retrieval & Professor Chat**: When questions are asked in chat, `VectorService` tokenizes passages, removes stop words, computes sublinear TF-IDF weights, and ranks chunks by Cosine Similarity. The top 5 matching passages are injected into the prompt with interactive `[Passage ID, p. page]` citations.
5. **Active Recall Generation & Assessment**: `StudyService` generates multiple-choice quizzes with distractor explanations, flip flashcards, and rubric-based essay evaluations.

---

## Core Features & User Perspective

- **Interactive Dynamic Concept Graph & Mind Map**:
  - Non-overlapping hierarchical layout separating root topic, primary pillars, and satellite subconcepts.
  - Interactive SVG canvas supporting smooth pan, cursor-anchored zoom, and view reset.
  - Draggable concept nodes with real-time cubic Bézier relationship curves updating at 60 FPS.
  - Grounded node inspection drawer displaying concept definitions, mechanisms, and verbatim source passages with page citations.
- **Grounded AI Professor Chat**:
  - In-browser TF-IDF Cosine Similarity retrieval ensuring answers are strictly derived from notes.
  - Structured pedagogical responses: **Core Takeaway**, **Detailed Breakdown**, and **Professor Exam Tip / Self-Check**.
  - Interactive inline citation badges linking claims directly to document passages.
- **Active Recall Study Suite**:
  - **Bloom's Taxonomy Quizzes**: Conceptual and analytical multiple-choice questions with full solution walkthroughs detailing why correct choices are right and why distractors are misconceptions.
  - **Interactive Flip Flashcards**: Active-recall front-and-back prompt cards for definitions and mechanisms.
  - **Academic Essay Grader**: Generates synthesized essay prompts and grades student responses on a 100-point rubric with actionable revision feedback.
- **Library Organization & Navigation**:
  - Folder-based subject categorization and soft-delete Trash management (restore or permanent purge).
  - Instant **Command Palette (`Ctrl + K`)** for quick navigation across documents and study tools.
  - Responsive dark/light theme switching with a distraction-free study layout.

---

## Data Structures Used

| Data Structure | Implementation | Primary Purpose | Memory & Complexity Notes |
| :--- | :--- | :--- | :--- |
| **Graph (Node-and-Edge Relational)** | `ConceptNode`, `ConceptEdge`, `List<ConceptNode>`, `List<ConceptEdge>` | Models non-linear semantic relationships; ensures full graph connectivity from root to subconcepts. | $O(V + E)$ space, bounded to 8–15 nodes per document, negligible WASM heap footprint. |
| **Hash Table / Associative Array** | `Dictionary<string, double>`, `Dictionary<string, int>`, `Dictionary<string, ConceptNode>` | Constant-time $O(1)$ term frequency counting, document frequency indexing, and concept label resolution. | Sparse representation avoids allocating dense term-document matrices, conserving browser memory. |
| **Hash Set** | `HashSet<string>`, `HashSet<int>` | $O(1)$ lookup for filtering 170+ English stop words, citation chunk validation, and visited graph vertex tracking. | Static read-only stop-word set allocated once per application lifecycle, minimizing GC overhead. |
| **Dynamic Array / List** | `List<DocumentItem>`, `List<SourceChunkItem>`, `List<QuizItem>`, `List<FlashcardItem>` | Contiguous memory storage, $O(1)$ amortized append, and indexed access for stratified passage sampling. | Only active document data resides in memory; inactive documents remain in SQLite WASM storage. |
| **Relational Database & B-Tree Indexes** | SQLite WASM (`sql.js`) backed by browser IndexedDB | Persistent, structured client-side storage for 8 relational tables (`documents`, `folders`, `source_chunks`, `concept_nodes`, etc.). | Primary key B-Tree indexing offloads persistence from volatile WASM memory to browser storage. |

---

## Algorithms & Asymptotic Complexity

### 1. TF-IDF Vectorization & Cosine Similarity Passage Ranking (`VectorService.RankPassages`)
Tokenizes query and document chunks, filters stop words, calculates sublinear term frequency $tf_{sub} = 1 + \ln(tf)$ and smoothed inverse document frequency $idf = \ln\left(\frac{N + 1}{df + 1}\right) + 1$, and computes cosine similarity:
$$\text{CosineSimilarity}(Q, D) = \frac{Q \cdot D}{\|Q\| \|D\|}$$
- **Time Complexity:** $O(N \cdot L + N \log N)$, where $N$ is passage count and $L$ is average tokens per passage (tokenization & dot product: $O(N \cdot L)$, sorting top scores: $O(N \log N)$).
- **Space Complexity:** $O(N \cdot U + V)$, where $U$ is average unique non-stopword tokens per passage and $V$ is vocabulary size.

### 2. Paragraph-Aware Sliding-Window Overlap Chunking (`DocumentService.ChunkText`)
Segments extracted text into chunks up to 1,200 characters (`ChunkSize = 1200`) with a 150-character sliding overlap (`ChunkOverlap = 150`). Splits on double newlines and applies a sliding window of step size 1,050 characters for oversized paragraphs.
- **Time Complexity:** $O(M)$, where $M$ is total document character length.
- **Space Complexity:** $O(M)$, storing generated `SourceChunkItem` substrings.

### 3. Stratified Uniform Representative Chunk Sampling (`VectorService.SelectRepresentativeChunks`)
Anchors the first two chunks (introduction), the final chunk (conclusion), and samples intermediate chunks at uniform fractional intervals $step = \frac{N - 2}{K - 3}$ (where $K \le 15$), followed by sorting by `ChunkIndex`.
- **Time Complexity:** $O(K \log K)$ when $N > K$ ($O(1)$ when $N \le K$).
- **Space Complexity:** $O(K)$, storing at most $K$ chunk references.

### 4. Radial Mind-Map Coordinate Layout & Connectivity (`ConceptService.ExtractConceptMapAsync`)
Places root node at center $(450, 320)$. Arranges primary concepts ($P$) along an ellipse using polar coordinates $x = \text{centerX} + R \cos(\theta_i)$, $y = \text{centerY} + 0.75 R \sin(\theta_i)$, where $\theta_i = \frac{2\pi i}{P} - \frac{\pi}{2}$. Satellite subconcepts are projected outward. Resolves unlinked nodes to nearest parents using Euclidean distance squared $(x_1 - x_2)^2 + (y_1 - y_2)^2$.
- **Time Complexity:** $O(E \cdot V + S \cdot P)$, where $V$ is total nodes, $E$ is relationships, $P$ is primary concepts, and $S$ is subconcepts.
- **Space Complexity:** $O(V + E)$ for vertices, edges, and lookup tables.

### 5. Regex Markdown-to-HTML Sanitization & Formatting (`TextFormatter.ToFormattedHtml`)
HTML-encodes raw AI responses via `WebUtility.HtmlEncode` to prevent XSS, then executes a pipeline of linear regex substitutions for code blocks, inline code, bold/italic, lists, blockquotes, and citation badges.
- **Time Complexity:** $O(L)$, where $L$ is markdown string character length.
- **Space Complexity:** $O(L)$, proportional to intermediate transformed strings.

---

## Tech Stack

- **Client Framework**: Blazor WebAssembly (.NET 10)
- **Programming Language**: C# 13 / .NET 10
- **Styling & Layout**: Tailwind CSS v3 (standalone CLI / PostCSS)
- **Local Relational Storage**: SQLite WebAssembly (`sql.js`) persisted via browser IndexedDB
- **Document Extraction**: Mozilla `pdf.js` (client-side PDF extraction)
- **Vector Retrieval**: Pure C# in-browser TF-IDF Cosine Similarity engine
- **Cloud LLM Inference**: [OpenRouter API](https://openrouter.ai/) (`google/gemma-4-26b-a4b-it:free`, `google/gemma-4-31b-it:free`, with auto-fallback to free providers like `baidu/ernie-5.5-flash-preview:free`, `nvidia/nemotron-3.5-super-vl:free`, `particle-ai/particle-1:free`) and optional Groq fallback

---

## User Manual & Getting Started

### Prerequisites

1. [.NET 10 SDK](https://dotnet.microsoft.com/download)
2. [Node.js](https://nodejs.org/) (for Tailwind CSS asset build)
3. A free [OpenRouter API Key](https://openrouter.ai/keys)

### Installation & Local Setup

1. **Clone the Repository:**
   ```bash
   git clone https://github.com/cjalanano-dev/notepad-ai-cmsc203.git
   cd BlazorApp1
   ```

2. **Configure Environment Variables:**
   Copy `.env.example` to `.env` in the project root:
   ```env
   OPENROUTER_API_KEY=sk-or-v1-your_openrouter_api_key_here
   OPENROUTER_MODEL=google/gemma-4-26b-a4b-it:free
   ```
   *(During build, values in `.env` automatically synchronize into `wwwroot/appsettings.Development.json` via MSBuild targets).*

3. **Run the Project:**
   ```bash
   dotnet run
   ```
   Navigate to `http://localhost:5189` in Google Chrome, Microsoft Edge, or Brave.

### Operating Guide

- **Step 1: Check AI Engine Connection**: Open **AI Engine / Settings** in the sidebar to verify your OpenRouter connection and selected model.
- **Step 2: Upload Documents**: In **Recent** or **Library**, upload a study file (`.pdf`, `.txt`, `.md`). Text is extracted, chunked, indexed, and stored in SQLite.
- **Step 3: Explore Concept Maps**: Open your uploaded reviewer to interact with the auto-generated radial concept tree. Pan, zoom, drag nodes, or click any node to view citations.
- **Step 4: Chat with AI Professor**: Ask questions in the Reviewer Chat. Answers feature structured takeaways and interactive `[Passage ID, p. page]` citation chips.
- **Step 5: Practice Active Recall**: Use the sidebar to launch multiple-choice quizzes with solution walkthroughs, flip flashcards, or draft essays for rubric grading.
- **Step 6: Organize Library**: Use folders, the search palette (`Ctrl + K`), and Trash to manage your study reviewers.

---

## Scope & Limitations

- **Client-Side Privacy & Persistence**: All document parsing (`pdf.js`), vector indexing (`VectorService`), and relational storage (`SQLite WASM`) execute 100% locally in your browser. No uploaded documents are ever stored on an external backend.
- **Cloud LLM Inference Requirement**: High-parameter models (26B–31B parameters) require significant GPU resources. To allow smooth, fast generation on standard student laptops and smartphones, LLM inference utilizes OpenRouter's cloud API. Previously generated concept maps, flashcards, quizzes, and chat messages remain permanently cached in local SQLite for full offline review.

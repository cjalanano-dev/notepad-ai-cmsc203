using BlazorApp1.Models;

namespace BlazorApp1.Services;

public class AppStateService
{
    private readonly OllamaService _ollamaService;
    private readonly DocumentService _documentService;
    private readonly ConceptService _conceptService;
    private readonly ChatService _chatService;
    private readonly StudyService _studyService;

    public event Action? OnChange;

    public string ActiveView { get; private set; } = "dashboard"; // dashboard, library, workspace, quiz, flashcards, essay, trash, settings
    public string Theme { get; private set; } = "dark"; // dark, light
    public bool IsSidebarCollapsed { get; set; } = false;
    public bool IsCommandPaletteOpen { get; set; } = false;
    public bool IsOriginalPreviewOpen { get; set; } = false;

    public UserProfile User { get; set; } = new UserProfile();
    public List<DocumentItem> Documents { get; set; } = new();
    public List<DocumentItem> TrashedDocuments { get; set; } = new();
    public List<ProjectFolder> Folders { get; set; } = new();

    public DocumentItem? SelectedDocument { get; set; }
    public ConceptNode? SelectedNode { get; set; }
    public ConceptDetail? SelectedDetail { get; set; }
    public List<ConceptNode> CurrentNodes { get; set; } = new();
    public List<ConceptEdge> CurrentEdges { get; set; } = new();
    public List<ChatMessage> CurrentMessages { get; set; } = new();

    // Study state
    public List<QuizItem> SampleQuiz { get; set; } = new();
    public List<FlashcardItem> SampleFlashcards { get; set; } = new();
    public EssayTask SampleEssay { get; set; } = new();

    // AI Status state
    public bool IsAiBusy { get; private set; } = false;
    public string AiBusyMessage { get; private set; } = string.Empty;
    public OllamaHealthResult OllamaStatus { get; private set; } = new();

    public AppStateService(
        OllamaService ollamaService,
        DocumentService documentService,
        ConceptService conceptService,
        ChatService chatService,
        StudyService studyService)
    {
        _ollamaService = ollamaService;
        _documentService = documentService;
        _conceptService = conceptService;
        _chatService = chatService;
        _studyService = studyService;

        SeedInitialData();
    }

    public async Task RefreshOllamaStatusAsync()
    {
        OllamaStatus = await _ollamaService.CheckHealthAsync();
        NotifyStateChanged();
    }

    public void SetView(string view)
    {
        ActiveView = view;
        NotifyStateChanged();
    }

    public void ToggleTheme()
    {
        Theme = Theme == "dark" ? "light" : "dark";
        NotifyStateChanged();
    }

    public void ToggleSidebar()
    {
        IsSidebarCollapsed = !IsSidebarCollapsed;
        NotifyStateChanged();
    }

    public void SelectDocument(int docId)
    {
        SelectedDocument = Documents.FirstOrDefault(d => d.Id == docId) ?? Documents.FirstOrDefault();
        if (SelectedDocument != null)
        {
            if (CurrentNodes.Count == 0 && SelectedDocument.Passages.Count > 0)
            {
                // Can trigger or keep default
            }
            LoadChatForDocument(SelectedDocument);
        }
        ActiveView = "workspace";
        NotifyStateChanged();
    }

    public void SelectNode(int nodeId)
    {
        SelectedNode = CurrentNodes.FirstOrDefault(n => n.Id == nodeId);
        if (SelectedNode != null && SelectedDocument != null)
        {
            var linkedSources = SelectedDocument.Passages
                .Where(p => SelectedNode.SourceChunkIds.Contains(p.Id))
                .ToList();

            if (linkedSources.Count == 0 && SelectedDocument.Passages.Count > 0)
            {
                linkedSources = SelectedDocument.Passages.Take(2).ToList();
            }

            SelectedDetail = new ConceptDetail
            {
                Id = SelectedNode.Id,
                Label = SelectedNode.Label,
                Explanation = SelectedNode.Explanation,
                NodeType = SelectedNode.NodeType,
                HasGeneratedExplanation = true,
                Sources = linkedSources
            };
        }
        NotifyStateChanged();
    }

    public void UpdateNodePosition(int nodeId, double x, double y)
    {
        var node = CurrentNodes.FirstOrDefault(n => n.Id == nodeId);
        if (node != null)
        {
            node.X = x;
            node.Y = y;
            NotifyStateChanged();
        }
    }

    public async Task AddDocumentWithContentAsync(string filename, string content)
    {
        var id = Documents.Count > 0 ? Documents.Max(d => d.Id) + 1 : 1;
        var coverIndex = (id % 8) + 1;
        var passages = _documentService.ProcessDocumentText(filename, content);

        var newDoc = new DocumentItem
        {
            Id = id,
            Filename = filename,
            UploadedAt = DateTime.Now,
            ChunkCount = passages.Count,
            ProcessingStatus = "processing",
            HasMap = false,
            CoverUrl = $"assets/notebook-covers/cover-0{coverIndex}.jpg",
            Passages = passages
        };

        Documents.Insert(0, newDoc);
        SelectedDocument = newDoc;
        NotifyStateChanged();

        try
        {
            IsAiBusy = true;
            AiBusyMessage = $"Extracting grounded concept map using {_ollamaService.DefaultModel}...";
            NotifyStateChanged();

            var (nodes, edges) = await _conceptService.ExtractConceptMapAsync(filename, passages);
            CurrentNodes = nodes;
            CurrentEdges = edges;
            newDoc.HasMap = true;
            newDoc.ProcessingStatus = "ready";

            if (nodes.Count > 0)
            {
                SelectNode(nodes[0].Id);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Concept map extraction error: {ex.Message}");
            newDoc.ProcessingStatus = "ready";
        }
        finally
        {
            IsAiBusy = false;
            AiBusyMessage = string.Empty;
            LoadChatForDocument(newDoc);
            NotifyStateChanged();
        }
    }

    public void MoveToTrash(int docId)
    {
        var doc = Documents.FirstOrDefault(d => d.Id == docId);
        if (doc != null)
        {
            Documents.Remove(doc);
            TrashedDocuments.Insert(0, doc);
            if (SelectedDocument?.Id == docId)
            {
                SelectedDocument = null;
            }
            NotifyStateChanged();
        }
    }

    public void RestoreFromTrash(int docId)
    {
        var doc = TrashedDocuments.FirstOrDefault(d => d.Id == docId);
        if (doc != null)
        {
            TrashedDocuments.Remove(doc);
            Documents.Insert(0, doc);
            NotifyStateChanged();
        }
    }

    public void DeleteForever(int docId)
    {
        var doc = TrashedDocuments.FirstOrDefault(d => d.Id == docId);
        if (doc != null)
        {
            TrashedDocuments.Remove(doc);
            NotifyStateChanged();
        }
    }

    public async Task AddChatMessageAsync(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return;

        CurrentMessages.Add(new ChatMessage
        {
            Role = "user",
            Content = text,
            Timestamp = DateTime.Now
        });
        NotifyStateChanged();

        if (SelectedDocument != null && SelectedDocument.Passages.Count > 0)
        {
            try
            {
                IsAiBusy = true;
                AiBusyMessage = $"Querying reviewer knowledge with {_ollamaService.DefaultModel}...";
                NotifyStateChanged();

                var aiResponse = await _chatService.AskReviewerAsync(
                    SelectedDocument.Filename,
                    SelectedDocument.Passages,
                    text,
                    CurrentMessages
                );
                CurrentMessages.Add(aiResponse);
            }
            catch (Exception ex)
            {
                CurrentMessages.Add(new ChatMessage
                {
                    Role = "assistant",
                    Content = $"Sorry, could not generate a verified answer from the reviewer: {ex.Message}",
                    Timestamp = DateTime.Now
                });
            }
            finally
            {
                IsAiBusy = false;
                AiBusyMessage = string.Empty;
                NotifyStateChanged();
            }
        }
        else
        {
            CurrentMessages.Add(new ChatMessage
            {
                Role = "assistant",
                Content = $"This document does not have extracted passages yet.",
                Timestamp = DateTime.Now
            });
            NotifyStateChanged();
        }
    }

    public async Task GenerateQuizForSelectedDocumentAsync()
    {
        if (SelectedDocument == null || SelectedDocument.Passages.Count == 0) return;
        try
        {
            IsAiBusy = true;
            AiBusyMessage = $"Generating grounded assessment with {_ollamaService.DefaultModel}...";
            NotifyStateChanged();

            SampleQuiz = await _studyService.GenerateQuizAsync(SelectedDocument.Filename, SelectedDocument.Passages);
        }
        finally
        {
            IsAiBusy = false;
            AiBusyMessage = string.Empty;
            NotifyStateChanged();
        }
    }

    public async Task GenerateFlashcardsForSelectedDocumentAsync()
    {
        if (SelectedDocument == null || SelectedDocument.Passages.Count == 0) return;
        try
        {
            IsAiBusy = true;
            AiBusyMessage = $"Synthesizing study flashcards with {_ollamaService.DefaultModel}...";
            NotifyStateChanged();

            SampleFlashcards = await _studyService.GenerateFlashcardsAsync(SelectedDocument.Filename, SelectedDocument.Passages);
        }
        finally
        {
            IsAiBusy = false;
            AiBusyMessage = string.Empty;
            NotifyStateChanged();
        }
    }

    public async Task GenerateEssayTaskForSelectedDocumentAsync()
    {
        if (SelectedDocument == null || SelectedDocument.Passages.Count == 0) return;
        try
        {
            IsAiBusy = true;
            AiBusyMessage = $"Formulating essay prompt with {_ollamaService.DefaultModel}...";
            NotifyStateChanged();

            SampleEssay = await _studyService.GenerateEssayTaskAsync(SelectedDocument.Filename, SelectedDocument.Passages);
        }
        finally
        {
            IsAiBusy = false;
            AiBusyMessage = string.Empty;
            NotifyStateChanged();
        }
    }

    public async Task GradeEssaySubmissionAsync(string submissionText)
    {
        if (SelectedDocument == null || SelectedDocument.Passages.Count == 0) return;
        try
        {
            IsAiBusy = true;
            AiBusyMessage = $"Grading essay with built-in 100-point rubric using {_ollamaService.DefaultModel}...";
            NotifyStateChanged();

            SampleEssay.Submission = submissionText;
            var (score, feedback) = await _studyService.GradeEssayAsync(
                SelectedDocument.Filename,
                SelectedDocument.Passages,
                SampleEssay.Prompt,
                submissionText
            );
            SampleEssay.Score = score;
            SampleEssay.Feedback = feedback;
        }
        finally
        {
            IsAiBusy = false;
            AiBusyMessage = string.Empty;
            NotifyStateChanged();
        }
    }

    private void SeedInitialData()
    {
        // Sample 1: Operating Systems Fundamentals
        var sampleOsText = 
@"# Introduction to Computer Operating Systems

Operating systems are system software that manage computer hardware, software resources, and provide common services for computer programs.

## Process Management
A process is an instance of a computer program that is being executed by one or many threads. The operating system kernel manages processes through Process Control Blocks (PCBs). The CPU scheduler allocates processor execution time among active processes using scheduling algorithms such as Round Robin, Priority Scheduling, and Completely Fair Scheduling.

## Memory Management and Virtual Memory
Memory management keeps track of the status of each memory location, whether allocated or free. Virtual memory creates an idealized abstraction of the storage resources that are actually available on a given machine. Paging is a memory management scheme by which a computer stores and retrieves data from secondary storage for use in main memory. When a process requests a page not currently resident in physical RAM, a Page Fault occurs, prompting the operating system to retrieve the missing page from disk swap space.

## File Systems and Persistent Storage
A file system controls how data is stored and retrieved. Without a file system, information placed in a storage medium would be one large body of data with no way to tell where one piece of information stops and where the next begins. Common file systems include ext4, NTFS, and APFS. In Unix-like operating systems, an inode is a data structure used to represent a filesystem object such as a file or a directory.

## Concurrency and Deadlocks
Concurrency involves the simultaneous execution of multiple instruction sequences. When threads concurrently access shared resources without proper synchronization, race conditions arise. To prevent conflicts, operating systems provide synchronization primitives like mutexes and semaphores. A deadlock is a state in which each member of a group of actions is waiting for some other member to take action, such as sending a message or releasing a lock. Four conditions are necessary for deadlock: mutual exclusion, hold and wait, no preemption, and circular wait.";

        var passages = _documentService.ProcessDocumentText("Operating Systems Fundamentals.txt", sampleOsText);

        Documents = new List<DocumentItem>
        {
            new DocumentItem
            {
                Id = 1,
                Filename = "Operating Systems Fundamentals.txt",
                UploadedAt = DateTime.Now.AddDays(-1),
                ChunkCount = passages.Count,
                ProcessingStatus = "ready",
                CoverUrl = "assets/notebook-covers/cover-01.jpg",
                Passages = passages
            },
            new DocumentItem
            {
                Id = 2,
                Filename = "Deep Learning Systems & Latent Graphs.pdf",
                UploadedAt = DateTime.Now.AddDays(-2),
                ChunkCount = 38,
                ProcessingStatus = "ready",
                CoverUrl = "assets/notebook-covers/cover-02.jpg",
                Passages = passages
            },
            new DocumentItem
            {
                Id = 3,
                Filename = "Distributed Operating Principles.pdf",
                UploadedAt = DateTime.Now.AddDays(-3),
                ChunkCount = 24,
                ProcessingStatus = "ready",
                CoverUrl = "assets/notebook-covers/cover-03.jpg",
                Passages = passages
            }
        };

        Folders = new List<ProjectFolder>
        {
            new ProjectFolder { Id = 1, Name = "Core Systems", CreatedAt = DateTime.Now.AddDays(-10), DocumentCount = 2 },
            new ProjectFolder { Id = 2, Name = "AI & Machine Learning", CreatedAt = DateTime.Now.AddDays(-14), DocumentCount = 1 },
            new ProjectFolder { Id = 3, Name = "Mathematics Foundations", CreatedAt = DateTime.Now.AddDays(-20), DocumentCount = 0 }
        };

        TrashedDocuments = new List<DocumentItem>
        {
            new DocumentItem { Id = 99, Filename = "Draft_Reviewer_Archive_Old.pdf", UploadedAt = DateTime.Now.AddDays(-12), ChunkCount = 8, ProcessingStatus = "ready" }
        };

        SelectedDocument = Documents.First();

        CurrentNodes = new List<ConceptNode>
        {
            new ConceptNode { Id = 1, Label = "Operating Systems", NodeType = "root", X = 350, Y = 60, Explanation = "System software that manages computer hardware, software resources, and provides common services for computer programs.", SourceChunkIds = new() { 1 } },
            new ConceptNode { Id = 2, Label = "Process Management", NodeType = "concept", X = 160, Y = 190, Explanation = "Managing instances of computer programs executed by threads using Process Control Blocks (PCBs) and CPU schedulers.", SourceChunkIds = new() { 1 } },
            new ConceptNode { Id = 3, Label = "Virtual Memory & Paging", NodeType = "concept", X = 500, Y = 190, Explanation = "An idealized abstraction storing data in secondary storage and paging into physical RAM, handling page faults.", SourceChunkIds = new() { 1 } },
            new ConceptNode { Id = 4, Label = "Concurrency & Deadlocks", NodeType = "subconcept", X = 200, Y = 350, Explanation = "Execution of multiple sequences where synchronization primitives (mutexes, semaphores) prevent race conditions and deadlocks.", SourceChunkIds = new() { 1 } },
            new ConceptNode { Id = 5, Label = "File Systems & Inodes", NodeType = "subconcept", X = 480, Y = 350, Explanation = "Structures such as inodes controlling how persistent data is organized and retrieved across physical media.", SourceChunkIds = new() { 1 } }
        };

        CurrentEdges = new List<ConceptEdge>
        {
            new ConceptEdge { Id = 1, SourceNodeId = 1, TargetNodeId = 2, Relationship = "orchestrates" },
            new ConceptEdge { Id = 2, SourceNodeId = 1, TargetNodeId = 3, Relationship = "manages" },
            new ConceptEdge { Id = 3, SourceNodeId = 2, TargetNodeId = 4, Relationship = "synchronizes" },
            new ConceptEdge { Id = 4, SourceNodeId = 3, TargetNodeId = 5, Relationship = "persists to" }
        };

        SelectNode(1);
        LoadChatForDocument(SelectedDocument);

        SampleQuiz = new List<QuizItem>
        {
            new QuizItem
            {
                Question = "What occurs when a process requests a memory page not currently resident in physical RAM?",
                Options = new List<string>
                {
                    "Page Fault prompting the OS to retrieve it from disk swap space",
                    "Immediate kernel panic and shutdown",
                    "Process termination without error",
                    "CPU registers are erased"
                },
                AnswerIndex = 0,
                Explanation = "When a requested page is not in physical RAM, a Page Fault occurs, causing the operating system to retrieve the page from disk swap space.",
                Citation = "p. 1"
            },
            new QuizItem
            {
                Question = "Which four conditions are strictly necessary for deadlock to occur?",
                Options = new List<string>
                {
                    "Mutual exclusion, hold and wait, no preemption, and circular wait",
                    "Paging, scheduling, segmentation, and thrashing",
                    "Race conditions, starvation, semaphores, and round robin",
                    "Process control, threads, heap allocation, and inode mapping"
                },
                AnswerIndex = 0,
                Explanation = "The four Coffman conditions necessary for deadlock are mutual exclusion, hold and wait, no preemption, and circular wait.",
                Citation = "p. 1"
            }
        };

        SampleFlashcards = new List<FlashcardItem>
        {
            new FlashcardItem
            {
                Front = "What is a Process Control Block (PCB)?",
                Back = "A kernel data structure storing information needed to manage a specific process, such as program counter and CPU registers.",
                Citation = "p. 1"
            },
            new FlashcardItem
            {
                Front = "What is an Inode in Unix-like operating systems?",
                Back = "A filesystem data structure used to represent an object such as a file or directory, storing metadata and block locations.",
                Citation = "p. 1"
            }
        };

        SampleEssay = new EssayTask
        {
            Title = "Concurrency Controls and Deadlock Mitigation",
            Prompt = "Explain how mutual exclusion and circular wait contribute to deadlocks in concurrent operating systems. Compare semaphores and mutexes as synchronization primitives.",
            Citation = "Passage p. 1",
            Submission = "Operating systems prevent deadlocks by eliminating at least one of the four necessary conditions. Mutexes provide mutual exclusion for critical sections, while semaphores manage multi-resource access.",
            Score = 95,
            Feedback = "Well-articulated explanation of Coffman conditions and the trade-offs between mutexes and semaphores."
        };
    }

    private void LoadChatForDocument(DocumentItem doc)
    {
        CurrentMessages = new List<ChatMessage>
        {
            new ChatMessage
            {
                Role = "assistant",
                Content = $"Hello! I am your AI reviewer companion for **{doc.Filename}** using **{_ollamaService.DefaultModel}**.\n\nYou can ask questions about concepts, request targeted explanations, or generate quizzes and study materials with verified source citations.",
                Timestamp = DateTime.Now.AddMinutes(-5)
            }
        };
    }

    public void NotifyStateChanged() => OnChange?.Invoke();
}

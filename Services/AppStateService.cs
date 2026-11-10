using BlazorApp1.Models;

namespace BlazorApp1.Services;

public class AppStateService
{
    private readonly ILlmService _ollamaService;
    private readonly DocumentService _documentService;
    private readonly ConceptService _conceptService;
    private readonly ChatService _chatService;
    private readonly StudyService _studyService;
    private readonly SqliteStorageService _sqliteService;

    public event Action? OnChange;

    public string ActiveView { get; private set; } = "dashboard"; // dashboard, library, workspace, quiz, flashcards, essay, trash, settings
    public string Theme { get; private set; } = "dark"; // dark, light
    public bool IsSidebarCollapsed { get; set; } = false;
    public bool IsMobileMenuOpen { get; set; } = false;
    public bool IsCommandPaletteOpen { get; set; } = false;
    public bool IsConceptModalOpen { get; set; } = false;
    public bool IsOriginalPreviewOpen { get; set; } = false;
    public bool IsDbLoaded { get; private set; } = false;

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
    public LlmHealthResult OllamaStatus { get; private set; } = new();
    public ILlmService LlmService => _ollamaService;

    public AppStateService(
        ILlmService ollamaService,
        DocumentService documentService,
        ConceptService conceptService,
        ChatService chatService,
        StudyService studyService,
        SqliteStorageService sqliteService)
    {
        _ollamaService = ollamaService;
        _documentService = documentService;
        _conceptService = conceptService;
        _chatService = chatService;
        _studyService = studyService;
        _sqliteService = sqliteService;

        SeedInitialData();
    }

    public async Task InitializeSqliteAsync()
    {
        if (IsDbLoaded) return;
        try
        {
            await _sqliteService.EnsureInitializedAsync();
            var savedDocs = await _sqliteService.GetAllDocumentsAsync();
            var savedFolders = await _sqliteService.GetAllFoldersAsync();
            Folders = savedFolders;

            foreach (var f in Folders)
            {
                f.DocumentCount = savedDocs.Count(d => d.ProjectId == f.Id);
            }

            if (savedDocs.Count > 0)
            {
                Documents = savedDocs;
                SelectedDocument = Documents.First();

                // Load SQLite map, chat, quizzes, and flashcards for first document
                var (nodes, edges) = await _sqliteService.GetConceptMapAsync(SelectedDocument.Id);
                if (nodes.Count > 0)
                {
                    CurrentNodes = nodes;
                    CurrentEdges = edges;
                    SelectNode(nodes[0].Id, openModal: false);
                }

                SampleQuiz = await _sqliteService.GetQuizzesAsync(SelectedDocument.Id);
                SampleFlashcards = await _sqliteService.GetFlashcardsAsync(SelectedDocument.Id);

                var msgs = await _sqliteService.GetChatMessagesAsync(SelectedDocument.Id);
                if (msgs.Count > 0)
                {
                    CurrentMessages = msgs;
                }
                else
                {
                    LoadChatForDocument(SelectedDocument);
                }
            }
            else
            {
                Documents = new List<DocumentItem>();
                SelectedDocument = null;
                CurrentNodes = new List<ConceptNode>();
                CurrentEdges = new List<ConceptEdge>();
                CurrentMessages = new List<ChatMessage>();
            }

            IsDbLoaded = true;
            NotifyStateChanged();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[AppState] SQLite initialization error: {ex.Message}");
        }
    }

    public async Task RefreshLlmStatusAsync()
    {
        OllamaStatus = await _ollamaService.CheckHealthAsync();
        NotifyStateChanged();
    }

    public void SetView(string view)
    {
        ActiveView = view;
        IsMobileMenuOpen = false;
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

    public void ToggleMobileMenu()
    {
        IsMobileMenuOpen = !IsMobileMenuOpen;
        NotifyStateChanged();
    }

    public void CloseMobileMenu()
    {
        if (IsMobileMenuOpen)
        {
            IsMobileMenuOpen = false;
            NotifyStateChanged();
        }
    }

    public void OpenCommandPalette()
    {
        IsCommandPaletteOpen = true;
        NotifyStateChanged();
    }

    public void CloseCommandPalette()
    {
        IsCommandPaletteOpen = false;
        NotifyStateChanged();
    }

    public void ToggleCommandPalette()
    {
        IsCommandPaletteOpen = !IsCommandPaletteOpen;
        NotifyStateChanged();
    }

    public async void SelectDocument(int docId)
    {
        SelectedDocument = Documents.FirstOrDefault(d => d.Id == docId) ?? Documents.FirstOrDefault();
        if (SelectedDocument != null)
        {
            // Load SQLite concept graph for this document
            var (nodes, edges) = await _sqliteService.GetConceptMapAsync(SelectedDocument.Id);
            if (nodes.Count > 0)
            {
                CurrentNodes = nodes;
                CurrentEdges = edges;
                SelectNode(nodes[0].Id, openModal: false);
            }
            else if (SelectedDocument.Passages.Count > 0)
            {
                // Fallback to sample graph if not yet generated
            }

            // Load SQLite chat messages for this document
            var msgs = await _sqliteService.GetChatMessagesAsync(SelectedDocument.Id);
            if (msgs.Count > 0)
            {
                CurrentMessages = msgs;
            }
            else
            {
                LoadChatForDocument(SelectedDocument);
            }

            // Load saved quizzes and flashcards for this document
            SampleQuiz = await _sqliteService.GetQuizzesAsync(SelectedDocument.Id);
            SampleFlashcards = await _sqliteService.GetFlashcardsAsync(SelectedDocument.Id);
        }
        ActiveView = "workspace";
        NotifyStateChanged();
    }

    public async Task CreateFolderAsync(string name)
    {
        name = name.Trim();
        if (string.IsNullOrWhiteSpace(name)) return;
        var folderId = await _sqliteService.InsertFolderAsync(name);
        Folders.Add(new ProjectFolder
        {
            Id = folderId,
            Name = name,
            CreatedAt = DateTime.Now,
            DocumentCount = 0
        });
        NotifyStateChanged();
    }

    public async Task DeleteFolderAsync(int folderId)
    {
        await _sqliteService.DeleteFolderAsync(folderId);
        Folders.RemoveAll(f => f.Id == folderId);
        foreach (var doc in Documents.Where(d => d.ProjectId == folderId))
        {
            doc.ProjectId = null;
        }
        NotifyStateChanged();
    }

    public async Task AssignDocumentToFolderAsync(int documentId, int? folderId)
    {
        await _sqliteService.AssignDocumentToFolderAsync(documentId, folderId);
        var doc = Documents.FirstOrDefault(d => d.Id == documentId);
        if (doc != null)
        {
            doc.ProjectId = folderId;
        }
        foreach (var f in Folders)
        {
            f.DocumentCount = Documents.Count(d => d.ProjectId == f.Id);
        }
        NotifyStateChanged();
    }

    public void SelectNode(int nodeId, bool openModal = true)
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
            IsConceptModalOpen = openModal;
        }
        else
        {
            IsConceptModalOpen = false;
        }
        NotifyStateChanged();
    }

    public void CloseConceptModal()
    {
        IsConceptModalOpen = false;
        NotifyStateChanged();
    }

    public async void UpdateNodePosition(int nodeId, double x, double y)
    {
        var node = CurrentNodes.FirstOrDefault(n => n.Id == nodeId);
        if (node != null)
        {
            node.X = x;
            node.Y = y;
            NotifyStateChanged();

            if (SelectedDocument != null)
            {
                await _sqliteService.ExecuteAsync(
                    "UPDATE concept_nodes SET x = ?, y = ? WHERE id = ?;",
                    x, y, nodeId
                );
            }
        }
    }

    public async Task AddDocumentWithContentAsync(string filename, string content)
    {
        var passages = _documentService.ProcessDocumentText(filename, content);
        var coverIndex = ((Documents.Count + 1) % 8) + 1;

        var newDoc = new DocumentItem
        {
            Filename = filename,
            UploadedAt = DateTime.Now,
            ChunkCount = passages.Count,
            ProcessingStatus = "processing",
            HasMap = false,
            CoverUrl = $"assets/notebook-covers/cover-0{coverIndex}.jpg",
            Passages = passages
        };

        // Persist document and chunks into SQLite database using SQL
        var insertedId = await _sqliteService.InsertDocumentAsync(newDoc, content);
        newDoc.Id = insertedId;

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

            // Save extracted nodes & edges to SQLite
            await _sqliteService.SaveConceptMapAsync(newDoc.Id, nodes, edges);
            await _sqliteService.UpdateDocumentStatusAsync(newDoc.Id, "ready", true);

            if (nodes.Count > 0)
            {
                SelectNode(nodes[0].Id, openModal: false);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Concept map extraction error: {ex.Message}");
            newDoc.ProcessingStatus = "ready";
            await _sqliteService.UpdateDocumentStatusAsync(newDoc.Id, "ready", false);
        }
        finally
        {
            IsAiBusy = false;
            AiBusyMessage = string.Empty;
            LoadChatForDocument(newDoc);
            NotifyStateChanged();
        }
    }

    public async void MoveToTrash(int docId)
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

            await _sqliteService.ExecuteAsync(
                "UPDATE documents SET processing_status = 'trashed' WHERE id = ?;",
                docId
            );
        }
    }

    public async void RestoreFromTrash(int docId)
    {
        var doc = TrashedDocuments.FirstOrDefault(d => d.Id == docId);
        if (doc != null)
        {
            TrashedDocuments.Remove(doc);
            Documents.Insert(0, doc);
            NotifyStateChanged();

            await _sqliteService.ExecuteAsync(
                "UPDATE documents SET processing_status = 'ready' WHERE id = ?;",
                docId
            );
        }
    }

    public async void DeleteForever(int docId)
    {
        var doc = TrashedDocuments.FirstOrDefault(d => d.Id == docId);
        if (doc != null)
        {
            TrashedDocuments.Remove(doc);
            NotifyStateChanged();

            await _sqliteService.DeleteDocumentAsync(docId);
        }
    }

    public async Task AddChatMessageAsync(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return;

        var userMsg = new ChatMessage
        {
            Role = "user",
            Content = text,
            Timestamp = DateTime.Now
        };
        CurrentMessages.Add(userMsg);
        NotifyStateChanged();

        if (SelectedDocument != null)
        {
            await _sqliteService.SaveChatMessageAsync(SelectedDocument.Id, userMsg);
        }

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
                if (SelectedDocument != null)
                {
                    await _sqliteService.SaveChatMessageAsync(SelectedDocument.Id, aiResponse);
                }
            }
            catch (Exception ex)
            {
                var errResponse = new ChatMessage
                {
                    Role = "assistant",
                    Content = $"Failed to get an answer: {ex.Message}",
                    Timestamp = DateTime.Now
                };
                CurrentMessages.Add(errResponse);
                if (SelectedDocument != null)
                {
                    await _sqliteService.SaveChatMessageAsync(SelectedDocument.Id, errResponse);
                }
            }
            finally
            {
                IsAiBusy = false;
                AiBusyMessage = string.Empty;
                NotifyStateChanged();
            }
        }
    }

    public async Task GenerateQuizForSelectedDocumentAsync()
    {
        if (SelectedDocument == null || SelectedDocument.Passages.Count == 0) return;
        try
        {
            IsAiBusy = true;
            AiBusyMessage = $"Generating grounded quiz questions with {_ollamaService.DefaultModel}...";
            NotifyStateChanged();

            var quiz = await _studyService.GenerateQuizAsync(SelectedDocument.Filename, SelectedDocument.Passages);
            if (quiz.Count > 0)
            {
                SampleQuiz = quiz;
                await _sqliteService.SaveQuizzesAsync(SelectedDocument.Id, quiz);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[GenerateQuiz Error] {ex.Message}");
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
            AiBusyMessage = $"Generating concept flashcards with {_ollamaService.DefaultModel}...";
            NotifyStateChanged();

            var cards = await _studyService.GenerateFlashcardsAsync(SelectedDocument.Filename, SelectedDocument.Passages);
            if (cards.Count > 0)
            {
                SampleFlashcards = cards;
                await _sqliteService.SaveFlashcardsAsync(SelectedDocument.Id, cards);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[GenerateFlashcards Error] {ex.Message}");
        }
        finally
        {
            IsAiBusy = false;
            AiBusyMessage = string.Empty;
            NotifyStateChanged();
        }
    }

    public async Task GradeEssaySubmissionAsync(string submission)
    {
        try
        {
            IsAiBusy = true;
            AiBusyMessage = $"Grading essay with {_ollamaService.DefaultModel}...";
            NotifyStateChanged();

            var docName = SelectedDocument?.Filename ?? SampleEssay.Title;
            var passages = SelectedDocument?.Passages ?? new List<SourceChunkItem>();

            var grade = await _studyService.GradeEssayAsync(
                docName,
                passages,
                SampleEssay.Prompt,
                submission
            );

            SampleEssay.Score = grade.Score;
            SampleEssay.Feedback = grade.Feedback;
        }
        catch (Exception ex)
        {
            SampleEssay.Feedback = $"Grading failed: {ex.Message}";
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
        Documents = new List<DocumentItem>();
        Folders = new List<ProjectFolder>();
        TrashedDocuments = new List<DocumentItem>();
        SelectedDocument = null;
        CurrentNodes = new List<ConceptNode>();
        CurrentEdges = new List<ConceptEdge>();
        CurrentMessages = new List<ChatMessage>();
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

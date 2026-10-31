using System.Text.Json;
using BlazorApp1.Models;
using Microsoft.JSInterop;

namespace BlazorApp1.Services;

public class SqliteStorageService
{
    private readonly IJSRuntime _jsRuntime;
    private bool _initialized;

    public SqliteStorageService(IJSRuntime jsRuntime)
    {
        _jsRuntime = jsRuntime;
    }

    public async Task EnsureInitializedAsync()
    {
        if (_initialized) return;
        try
        {
            await _jsRuntime.InvokeVoidAsync("sqliteStorage.init");
            _initialized = true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[SQLite Init Exception] {ex.Message}");
        }
    }

    public async Task ExecuteAsync(string sql, params object[] parameters)
    {
        await EnsureInitializedAsync();
        try
        {
            await _jsRuntime.InvokeVoidAsync("sqliteStorage.executeSql", sql, parameters);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[SQLite Execute Error] {sql}: {ex.Message}");
        }
    }

    public async Task<List<T>> QueryAsync<T>(string sql, params object[] parameters)
    {
        await EnsureInitializedAsync();
        try
        {
            var json = await _jsRuntime.InvokeAsync<string>("sqliteStorage.querySql", sql, parameters);
            if (string.IsNullOrWhiteSpace(json) || json == "[]") return new List<T>();
            return JsonSerializer.Deserialize<List<T>>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            }) ?? new List<T>();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[SQLite Query Error] {sql}: {ex.Message}");
            return new List<T>();
        }
    }

    // Direct typed persistence helpers using SQL queries
    public async Task<List<DocumentItem>> GetAllDocumentsAsync()
    {
        var rawDocs = await QueryAsync<SqlDocumentRow>("SELECT * FROM documents ORDER BY id DESC;");
        var docs = new List<DocumentItem>();

        foreach (var r in rawDocs)
        {
            var passages = await GetPassagesByDocumentIdAsync(r.Id);
            docs.Add(new DocumentItem
            {
                Id = r.Id,
                Filename = r.Filename,
                UploadedAt = DateTime.TryParse(r.Uploaded_At, out var dt) ? dt : DateTime.Now,
                ProcessingStatus = r.Processing_Status ?? "ready",
                ChunkCount = r.Chunk_Count,
                HasMap = r.Has_Map == 1,
                CoverUrl = r.Cover_Url,
                Passages = passages
            });
        }

        return docs;
    }

    public async Task<int> InsertDocumentAsync(DocumentItem doc, string rawText = "")
    {
        await ExecuteAsync(
            "INSERT INTO documents (filename, uploaded_at, processing_status, chunk_count, has_map, cover_url, raw_text) VALUES (?, ?, ?, ?, ?, ?, ?);",
            doc.Filename,
            doc.UploadedAt.ToString("o"),
            doc.ProcessingStatus,
            doc.ChunkCount,
            doc.HasMap ? 1 : 0,
            doc.CoverUrl ?? "assets/notebook-covers/cover-01.jpg",
            rawText
        );

        var lastIdList = await QueryAsync<SqlIdRow>("SELECT last_insert_rowid() as id;");
        var docId = lastIdList.FirstOrDefault()?.Id ?? doc.Id;
        doc.Id = docId;

        // Persist chunks using SQL
        foreach (var chunk in doc.Passages)
        {
            await ExecuteAsync(
                "INSERT INTO source_chunks (document_id, page_number, chunk_index, content, document_filename) VALUES (?, ?, ?, ?, ?);",
                docId, chunk.PageNumber, chunk.ChunkIndex, chunk.Content, doc.Filename
            );
        }

        return docId;
    }

    public async Task UpdateDocumentStatusAsync(int docId, string status, bool hasMap)
    {
        await ExecuteAsync(
            "UPDATE documents SET processing_status = ?, has_map = ? WHERE id = ?;",
            status, hasMap ? 1 : 0, docId
        );
    }

    public async Task DeleteDocumentAsync(int docId)
    {
        await ExecuteAsync("DELETE FROM documents WHERE id = ?;", docId);
        await ExecuteAsync("DELETE FROM source_chunks WHERE document_id = ?;", docId);
        await ExecuteAsync("DELETE FROM concept_nodes WHERE document_id = ?;", docId);
        await ExecuteAsync("DELETE FROM concept_edges WHERE document_id = ?;", docId);
        await ExecuteAsync("DELETE FROM chat_messages WHERE document_id = ?;", docId);
    }

    public async Task<List<SourceChunkItem>> GetPassagesByDocumentIdAsync(int docId)
    {
        var rows = await QueryAsync<SqlChunkRow>(
            "SELECT id, page_number as PageNumber, chunk_index as ChunkIndex, content, document_filename as DocumentFilename FROM source_chunks WHERE document_id = ? ORDER BY id ASC;",
            docId
        );

        return rows.Select(r => new SourceChunkItem
        {
            Id = r.Id,
            PageNumber = r.PageNumber,
            ChunkIndex = r.ChunkIndex,
            Content = r.Content,
            DocumentFilename = r.DocumentFilename
        }).ToList();
    }

    public async Task SaveConceptMapAsync(int docId, List<ConceptNode> nodes, List<ConceptEdge> edges)
    {
        await ExecuteAsync("DELETE FROM concept_nodes WHERE document_id = ?;", docId);
        await ExecuteAsync("DELETE FROM concept_edges WHERE document_id = ?;", docId);

        // Map original memory IDs to newly generated SQLite row IDs
        var idMap = new Dictionary<int, int>();

        foreach (var node in nodes)
        {
            var chunkIdsStr = string.Join(",", node.SourceChunkIds ?? new List<int>());
            await ExecuteAsync(
                "INSERT INTO concept_nodes (document_id, label, explanation, node_type, x, y, source_chunk_ids) VALUES (?, ?, ?, ?, ?, ?, ?);",
                docId, node.Label, node.Explanation, node.NodeType, node.X, node.Y, chunkIdsStr
            );

            var lastIdList = await QueryAsync<SqlIdRow>("SELECT last_insert_rowid() as id;");
            var newId = lastIdList.FirstOrDefault()?.Id ?? node.Id;
            idMap[node.Id] = newId;
            node.Id = newId;
        }

        foreach (var edge in edges)
        {
            var newSourceId = idMap.TryGetValue(edge.SourceNodeId, out var sId) ? sId : edge.SourceNodeId;
            var newTargetId = idMap.TryGetValue(edge.TargetNodeId, out var tId) ? tId : edge.TargetNodeId;

            edge.SourceNodeId = newSourceId;
            edge.TargetNodeId = newTargetId;

            await ExecuteAsync(
                "INSERT INTO concept_edges (document_id, source_node_id, target_node_id, relationship) VALUES (?, ?, ?, ?);",
                docId, newSourceId, newTargetId, edge.Relationship
            );
        }
    }

    public async Task<(List<ConceptNode> Nodes, List<ConceptEdge> Edges)> GetConceptMapAsync(int docId)
    {
        var rawNodes = await QueryAsync<SqlNodeRow>(
            "SELECT id, label, explanation, node_type as NodeType, x, y, source_chunk_ids as SourceChunkIds FROM concept_nodes WHERE document_id = ?;",
            docId
        );

        var nodes = rawNodes.Select(n => new ConceptNode
        {
            Id = n.Id,
            Label = n.Label,
            Explanation = n.Explanation ?? "",
            NodeType = n.NodeType ?? "concept",
            X = n.X,
            Y = n.Y,
            SourceChunkIds = string.IsNullOrWhiteSpace(n.SourceChunkIds)
                ? new List<int>()
                : n.SourceChunkIds.Split(',', StringSplitOptions.RemoveEmptyEntries)
                                  .Select(s => int.TryParse(s, out var v) ? v : 0)
                                  .Where(v => v > 0).ToList()
        }).ToList();

        var rawEdges = await QueryAsync<SqlEdgeRow>(
            "SELECT id, source_node_id as SourceNodeId, target_node_id as TargetNodeId, relationship FROM concept_edges WHERE document_id = ?;",
            docId
        );

        var edges = rawEdges.Select(e => new ConceptEdge
        {
            Id = e.Id,
            SourceNodeId = e.SourceNodeId,
            TargetNodeId = e.TargetNodeId,
            Relationship = e.Relationship ?? "relates to"
        }).ToList();

        return (nodes, edges);
    }

    public async Task SaveChatMessageAsync(int docId, ChatMessage msg)
    {
        var citJson = JsonSerializer.Serialize(msg.Citations);
        await ExecuteAsync(
            "INSERT INTO chat_messages (document_id, role, content, timestamp, citations_json) VALUES (?, ?, ?, ?, ?);",
            docId, msg.Role, msg.Content, msg.Timestamp.ToString("o"), citJson
        );
    }

    public async Task<List<ChatMessage>> GetChatMessagesAsync(int docId)
    {
        var raw = await QueryAsync<SqlChatRow>(
            "SELECT id, role, content, timestamp, citations_json as CitationsJson FROM chat_messages WHERE document_id = ? ORDER BY id ASC;",
            docId
        );

        return raw.Select(r => new ChatMessage
        {
            Role = r.Role,
            Content = r.Content,
            Timestamp = DateTime.TryParse(r.Timestamp, out var dt) ? dt : DateTime.Now,
            Citations = string.IsNullOrWhiteSpace(r.CitationsJson)
                ? new List<ReviewerCitation>()
                : JsonSerializer.Deserialize<List<ReviewerCitation>>(r.CitationsJson) ?? new List<ReviewerCitation>()
        }).ToList();
    }

    private class SqlDocumentRow
    {
        public int Id { get; set; }
        public string Filename { get; set; } = "";
        public string Uploaded_At { get; set; } = "";
        public string Processing_Status { get; set; } = "ready";
        public int Chunk_Count { get; set; }
        public int Has_Map { get; set; }
        public string? Cover_Url { get; set; }
    }

    private class SqlChunkRow
    {
        public int Id { get; set; }
        public int PageNumber { get; set; }
        public int ChunkIndex { get; set; }
        public string Content { get; set; } = "";
        public string? DocumentFilename { get; set; }
    }

    private class SqlNodeRow
    {
        public int Id { get; set; }
        public string Label { get; set; } = "";
        public string? Explanation { get; set; }
        public string? NodeType { get; set; }
        public double X { get; set; }
        public double Y { get; set; }
        public string? SourceChunkIds { get; set; }
    }

    private class SqlEdgeRow
    {
        public int Id { get; set; }
        public int SourceNodeId { get; set; }
        public int TargetNodeId { get; set; }
        public string? Relationship { get; set; }
    }

    private class SqlChatRow
    {
        public int Id { get; set; }
        public string Role { get; set; } = "";
        public string Content { get; set; } = "";
        public string Timestamp { get; set; } = "";
        public string? CitationsJson { get; set; }
    }

    private class SqlIdRow
    {
        public int Id { get; set; }
    }
}


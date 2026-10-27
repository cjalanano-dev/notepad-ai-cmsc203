using BlazorApp1.Models;

namespace BlazorApp1.Services;

public class ConceptService
{
    private readonly OllamaService _ollama;

    public ConceptService(OllamaService ollama)
    {
        _ollama = ollama;
    }

    public static string BuildPromptPassages(List<SourceChunkItem> chunks)
    {
        return string.Join("\n", chunks.Select(c => $"--- [Passage ID: {c.Id}] (Page {c.PageNumber}) ---\n{c.Content}\n"));
    }

    public async Task<(List<ConceptNode> Nodes, List<ConceptEdge> Edges)> ExtractConceptMapAsync(
        string docTitle,
        List<SourceChunkItem> allChunks,
        string? model = null,
        CancellationToken ct = default)
    {
        if (allChunks.Count == 0)
        {
            throw new ArgumentException("No chunks supplied for concept extraction.");
        }

        var validChunkIds = allChunks.Select(c => c.Id).ToHashSet();
        var selectedChunks = VectorService.SelectRepresentativeChunks(allChunks, 15);
        var passagesText = BuildPromptPassages(selectedChunks);

        var systemPrompt = 
            "You are an offline, document-grounded concept extraction engine. " +
            "Your task is to identify key educational concepts and relationships using ONLY the supplied numbered source passages.\n" +
            "STRICT GROUNDING RULES:\n" +
            "1. Every concept must include at least one valid supporting passage ID from the provided text in 'source_passage_ids'.\n" +
            "2. Do NOT introduce external knowledge, facts, or entities not explicitly present in the source passages.\n" +
            "3. Provide a non-empty, clear, beginner-friendly explanation of 2 to 4 sentences for every concept, strictly grounded in the document.\n" +
            "4. Assign node_type as 'root' for the overarching main topic, 'concept' for key core concepts, and 'subconcept' for supporting details.\n" +
            "5. Extract 5 to 12 meaningful concepts.\n" +
            "6. In 'relationships', describe valid connections between concepts (e.g. 'source_label', 'target_label', 'relationship'). Both source and target must be exact concept labels.\n" +
            "7. Return JSON strictly conforming to this schema:\n" +
            "{\n" +
            "  \"title\": \"string\",\n" +
            "  \"concepts\": [{\"label\": \"string\", \"explanation\": \"string\", \"node_type\": \"root|concept|subconcept\", \"source_passage_ids\": [1, 2]}],\n" +
            "  \"relationships\": [{\"source_label\": \"string\", \"target_label\": \"string\", \"relationship\": \"string\"}]\n" +
            "}";

        var userPrompt = $"Document: {docTitle}\n\nSource Passages:\n{passagesText}\n\nGenerate the structured concept map JSON now:";

        var extraction = await _ollama.GenerateStructuredAsync<ExtractionResult>(systemPrompt, userPrompt, model, ct);
        if (extraction == null || extraction.Concepts == null || extraction.Concepts.Count == 0)
        {
            throw new InvalidOperationException("Model failed to extract concept map.");
        }

        var nodes = new List<ConceptNode>();
        var edges = new List<ConceptEdge>();
        var labelToNode = new Dictionary<string, ConceptNode>(StringComparer.OrdinalIgnoreCase);

        int nodeId = 1;
        // SVG Layout positioning coordinates
        double rootX = 350, rootY = 70;
        int conceptCount = 0;
        int subconceptCount = 0;

        foreach (var raw in extraction.Concepts)
        {
            var validSources = raw.SourcePassageIds.Where(id => validChunkIds.Contains(id)).ToList();
            if (validSources.Count == 0 && allChunks.Count > 0)
            {
                validSources.Add(allChunks[0].Id);
            }

            var nodeType = raw.NodeType?.ToLowerInvariant() ?? "concept";
            if (nodeType != "root" && nodeType != "concept" && nodeType != "subconcept")
            {
                nodeType = "concept";
            }

            double x, y;
            if (nodeType == "root")
            {
                x = rootX;
                y = rootY;
            }
            else if (nodeType == "concept")
            {
                conceptCount++;
                x = 130 + (conceptCount * 170) % 550;
                y = 190 + ((conceptCount % 2) * 30);
            }
            else
            {
                subconceptCount++;
                x = 100 + (subconceptCount * 140) % 580;
                y = 350 + ((subconceptCount % 2) * 40);
            }

            var node = new ConceptNode
            {
                Id = nodeId++,
                Label = raw.Label.Trim(),
                Explanation = raw.Explanation.Trim(),
                NodeType = nodeType,
                X = x,
                Y = y,
                SourceChunkIds = validSources
            };

            nodes.Add(node);
            labelToNode[node.Label] = node;
        }

        int edgeId = 1;
        if (extraction.Relationships != null)
        {
            foreach (var r in extraction.Relationships)
            {
                if (labelToNode.TryGetValue(r.SourceLabel.Trim(), out var srcNode) &&
                    labelToNode.TryGetValue(r.TargetLabel.Trim(), out var tgtNode) &&
                    srcNode.Id != tgtNode.Id)
                {
                    edges.Add(new ConceptEdge
                    {
                        Id = edgeId++,
                        SourceNodeId = srcNode.Id,
                        TargetNodeId = tgtNode.Id,
                        Relationship = string.IsNullOrWhiteSpace(r.Relationship) ? "relates to" : r.Relationship.Trim()
                    });
                }
            }
        }

        // Ensure edges if none found
        if (edges.Count == 0 && nodes.Count > 1)
        {
            var root = nodes.FirstOrDefault(n => n.NodeType == "root") ?? nodes[0];
            foreach (var n in nodes.Where(x => x.Id != root.Id))
            {
                edges.Add(new ConceptEdge
                {
                    Id = edgeId++,
                    SourceNodeId = root.Id,
                    TargetNodeId = n.Id,
                    Relationship = "includes"
                });
            }
        }

        return (nodes, edges);
    }

    public async Task<string> GenerateConceptExplanationAsync(
        string conceptLabel,
        string docTitle,
        List<SourceChunkItem> passages,
        string? model = null,
        CancellationToken ct = default)
    {
        var sourceText = string.Join("\n\n", passages.Select(p => $"[Passage {p.Id}, page {p.PageNumber}]\n{p.Content}"));
        var systemPrompt =
            "You are a patient tutor. Explain the named concept in 2 to 4 clear, " +
            "beginner-friendly sentences using only the supplied reviewer passages. " +
            "Explain what it means and how it works when the source supports that. " +
            "Do not add outside facts or guess. Cite supporting ideas with the given " +
            "page markers, such as [p. 2]. If the passages do not explain the concept, " +
            "say what information is missing. Return only the explanation.";

        var userPrompt = $"Reviewer: {docTitle}\nConcept: {conceptLabel}\n\nLinked reviewer passages:\n{sourceText}";

        return await _ollama.GenerateTextAsync(systemPrompt, userPrompt, model, ct);
    }
}

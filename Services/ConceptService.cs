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
            "You are a mind-map generator. You build a hierarchical, connected radial mind-map from the text.\n" +
            "RULES:\n" +
            "1. Extract 1 primary central topic as node_type='root'.\n" +
            "2. Extract 4 to 8 primary subtopics as node_type='concept'.\n" +
            "3. Extract 3 to 6 detailed child concepts as node_type='subconcept'.\n" +
            "4. Every concept must have an explanation of 2 to 3 sentences grounded in the text and valid 'source_passage_ids'.\n" +
            "5. CRITICAL: Every single concept and subconcept MUST be connected in 'relationships'. No orphaned nodes.\n" +
            "   - Connect 'root' to each 'concept'.\n" +
            "   - Connect each 'concept' to its child 'subconcept' nodes.\n" +
            "   - Both 'source_label' and 'target_label' MUST match the exact concept labels defined in 'concepts'.\n" +
            "   - 'relationship' should be a concise active verb or phrase (e.g. 'comprises', 'regulates', 'leads to', 'consists of').\n" +
            "6. Return strictly JSON conforming to this structure:\n" +
            "{\n" +
            "  \"title\": \"string\",\n" +
            "  \"concepts\": [{\"label\": \"string\", \"explanation\": \"string\", \"node_type\": \"root|concept|subconcept\", \"source_passage_ids\": [1]}],\n" +
            "  \"relationships\": [{\"source_label\": \"string\", \"target_label\": \"string\", \"relationship\": \"string\"}]\n" +
            "}";

        var userPrompt = $"Document: {docTitle}\n\nSource Passages:\n{passagesText}\n\nGenerate the connected mind map JSON now:";

        ExtractionResult? extraction = null;
        try
        {
            extraction = await _ollama.GenerateStructuredAsync<ExtractionResult>(systemPrompt, userPrompt, model, ct);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ConceptService] LLM extraction error: {ex.Message}");
        }

        // If extraction failed or returned no concepts, build a deterministic fallback mind map from text
        if (extraction == null || extraction.Concepts == null || extraction.Concepts.Count == 0)
        {
            extraction = GenerateFallbackMindMap(docTitle, allChunks);
        }

        // Ensure at least one root node exists
        if (!extraction.Concepts.Any(c => c.NodeType?.ToLowerInvariant() == "root"))
        {
            if (extraction.Concepts.Count > 0)
            {
                extraction.Concepts[0].NodeType = "root";
            }
            else
            {
                extraction.Concepts.Add(new RawConcept
                {
                    Label = Path.GetFileNameWithoutExtension(docTitle),
                    Explanation = $"Primary document overview for {docTitle}.",
                    NodeType = "root",
                    SourcePassageIds = allChunks.Take(2).Select(c => c.Id).ToList()
                });
            }
        }

        var nodes = new List<ConceptNode>();
        var labelToNode = new Dictionary<string, ConceptNode>(StringComparer.OrdinalIgnoreCase);

        // Radial Mind Map Coordinate Calculation (Central Root with radiating branches)
        const double centerX = 450;
        const double centerY = 320;

        var rawRoot = extraction.Concepts.FirstOrDefault(c => c.NodeType?.ToLowerInvariant() == "root") ?? extraction.Concepts[0];
        var rootNode = new ConceptNode
        {
            Id = 1,
            Label = rawRoot.Label.Trim(),
            Explanation = rawRoot.Explanation?.Trim() ?? "",
            NodeType = "root",
            X = centerX,
            Y = centerY,
            SourceChunkIds = rawRoot.SourcePassageIds.Where(id => validChunkIds.Contains(id)).ToList()
        };
        if (rootNode.SourceChunkIds.Count == 0 && allChunks.Count > 0)
        {
            rootNode.SourceChunkIds.Add(allChunks[0].Id);
        }
        nodes.Add(rootNode);
        labelToNode[rootNode.Label] = rootNode;

        var otherConcepts = extraction.Concepts
            .Where(c => !string.Equals(c.Label.Trim(), rootNode.Label, StringComparison.OrdinalIgnoreCase))
            .ToList();

        var primaryBranches = otherConcepts.Where(c => c.NodeType?.ToLowerInvariant() != "subconcept").ToList();
        var subBranches = otherConcepts.Where(c => c.NodeType?.ToLowerInvariant() == "subconcept").ToList();

        // If no primary branches distinguished, split evenly
        if (primaryBranches.Count == 0 && otherConcepts.Count > 0)
        {
            primaryBranches = otherConcepts.Take(Math.Min(6, otherConcepts.Count)).ToList();
            subBranches = otherConcepts.Skip(primaryBranches.Count).ToList();
        }

        int nextId = 2;
        const double primaryRadius = 260;
        double branchAngleStep = primaryBranches.Count > 0 ? (2 * Math.PI) / primaryBranches.Count : 1;

        // Position Primary Concept Nodes around the root in a radial mind map circle
        for (int i = 0; i < primaryBranches.Count; i++)
        {
            var raw = primaryBranches[i];
            double angle = i * branchAngleStep - (Math.PI / 2); // Start at top
            double x = Math.Round(centerX + primaryRadius * Math.Cos(angle));
            double y = Math.Round(centerY + (primaryRadius * 0.75) * Math.Sin(angle)); // ellipse for 16:9 canvas

            var node = new ConceptNode
            {
                Id = nextId++,
                Label = raw.Label.Trim(),
                Explanation = raw.Explanation?.Trim() ?? "",
                NodeType = "concept",
                X = x,
                Y = y,
                SourceChunkIds = raw.SourcePassageIds.Where(id => validChunkIds.Contains(id)).ToList()
            };
            if (node.SourceChunkIds.Count == 0 && allChunks.Count > 0)
            {
                node.SourceChunkIds.Add(allChunks[Math.Min(i, allChunks.Count - 1)].Id);
            }

            nodes.Add(node);
            labelToNode[node.Label] = node;
        }

        // Position Subconcepts branching outward from primary concepts
        for (int i = 0; i < subBranches.Count; i++)
        {
            var raw = subBranches[i];
            var parentIndex = i % (primaryBranches.Count > 0 ? primaryBranches.Count : 1);
            var parentNode = primaryBranches.Count > parentIndex ? labelToNode.GetValueOrDefault(primaryBranches[parentIndex].Label.Trim()) : null;

            double parentX = parentNode?.X ?? centerX;
            double parentY = parentNode?.Y ?? centerY;
            double angleOutward = Math.Atan2(parentY - centerY, parentX - centerX) + ((i % 2 == 0 ? 0.35 : -0.35));
            const double subRadius = 140;

            double x = Math.Round(parentX + subRadius * Math.Cos(angleOutward));
            double y = Math.Round(parentY + subRadius * Math.Sin(angleOutward));

            var node = new ConceptNode
            {
                Id = nextId++,
                Label = raw.Label.Trim(),
                Explanation = raw.Explanation?.Trim() ?? "",
                NodeType = "subconcept",
                X = x,
                Y = y,
                SourceChunkIds = raw.SourcePassageIds.Where(id => validChunkIds.Contains(id)).ToList()
            };
            if (node.SourceChunkIds.Count == 0 && allChunks.Count > 0)
            {
                node.SourceChunkIds.Add(allChunks[Math.Min(i, allChunks.Count - 1)].Id);
            }

            nodes.Add(node);
            labelToNode[node.Label] = node;
        }

        // Resolve Edges & Guarantee Full Mind Map Connectivity
        var edges = new List<ConceptEdge>();
        var connectedNodeIds = new HashSet<int>();
        int edgeId = 1;

        if (extraction.Relationships != null)
        {
            foreach (var r in extraction.Relationships)
            {
                if (string.IsNullOrWhiteSpace(r.SourceLabel) || string.IsNullOrWhiteSpace(r.TargetLabel)) continue;

                // Fuzzy match or exact match labels
                var srcNode = FindMatchingNode(r.SourceLabel, labelToNode);
                var tgtNode = FindMatchingNode(r.TargetLabel, labelToNode);

                if (srcNode != null && tgtNode != null && srcNode.Id != tgtNode.Id)
                {
                    // Prevent duplicate reciprocal edges
                    if (!edges.Any(e => (e.SourceNodeId == srcNode.Id && e.TargetNodeId == tgtNode.Id) ||
                                        (e.SourceNodeId == tgtNode.Id && e.TargetNodeId == srcNode.Id)))
                    {
                        edges.Add(new ConceptEdge
                        {
                            Id = edgeId++,
                            SourceNodeId = srcNode.Id,
                            TargetNodeId = tgtNode.Id,
                            Relationship = string.IsNullOrWhiteSpace(r.Relationship) ? "connects to" : r.Relationship.Trim()
                        });
                        connectedNodeIds.Add(srcNode.Id);
                        connectedNodeIds.Add(tgtNode.Id);
                    }
                }
            }
        }

        // Connect Root to each Primary Concept that has no edge
        var primaryNodes = nodes.Where(n => n.NodeType == "concept").ToList();
        foreach (var pNode in primaryNodes)
        {
            if (!edges.Any(e => (e.SourceNodeId == rootNode.Id && e.TargetNodeId == pNode.Id) ||
                                (e.SourceNodeId == pNode.Id && e.TargetNodeId == rootNode.Id)))
            {
                edges.Add(new ConceptEdge
                {
                    Id = edgeId++,
                    SourceNodeId = rootNode.Id,
                    TargetNodeId = pNode.Id,
                    Relationship = "branches to"
                });
                connectedNodeIds.Add(pNode.Id);
            }
        }

        // Connect any remaining orphaned Subconcept to the closest primary concept
        var subNodes = nodes.Where(n => n.NodeType == "subconcept").ToList();
        foreach (var sNode in subNodes)
        {
            if (!connectedNodeIds.Contains(sNode.Id) || !edges.Any(e => e.SourceNodeId == sNode.Id || e.TargetNodeId == sNode.Id))
            {
                // Find nearest primary node by distance
                var nearest = primaryNodes
                    .OrderBy(p => Math.Pow(p.X - sNode.X, 2) + Math.Pow(p.Y - sNode.Y, 2))
                    .FirstOrDefault() ?? rootNode;

                edges.Add(new ConceptEdge
                {
                    Id = edgeId++,
                    SourceNodeId = nearest.Id,
                    TargetNodeId = sNode.Id,
                    Relationship = "details"
                });
                connectedNodeIds.Add(sNode.Id);
            }
        }

        return (nodes, edges);
    }

    private ConceptNode? FindMatchingNode(string label, Dictionary<string, ConceptNode> lookup)
    {
        var trimmed = label.Trim();
        if (lookup.TryGetValue(trimmed, out var exact)) return exact;

        // Partial match
        foreach (var kvp in lookup)
        {
            if (kvp.Key.Contains(trimmed, StringComparison.OrdinalIgnoreCase) ||
                trimmed.Contains(kvp.Key, StringComparison.OrdinalIgnoreCase))
            {
                return kvp.Value;
            }
        }
        return null;
    }

    private ExtractionResult GenerateFallbackMindMap(string docTitle, List<SourceChunkItem> chunks)
    {
        var titleClean = Path.GetFileNameWithoutExtension(docTitle).Replace("-", " ").Replace("_", " ");
        var result = new ExtractionResult
        {
            Title = titleClean,
            Concepts = new List<RawConcept>
            {
                new RawConcept
                {
                    Label = titleClean,
                    Explanation = $"Central topic analyzing {titleClean}.",
                    NodeType = "root",
                    SourcePassageIds = chunks.Take(2).Select(c => c.Id).ToList()
                }
            },
            Relationships = new List<RawRelationship>()
        };

        // Extract key paragraphs as concept branches
        var branchIndex = 1;
        for (int i = 0; i < Math.Min(6, chunks.Count); i++)
        {
            var chunk = chunks[i];
            var firstLine = chunk.Content.Split('\n', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "";
            var label = firstLine.Trim().TrimStart('#', '-', '*', ' ');
            if (label.Length > 28) label = label.Substring(0, 28) + "...";
            if (string.IsNullOrWhiteSpace(label)) label = $"Topic Section {branchIndex}";

            result.Concepts.Add(new RawConcept
            {
                Label = label,
                Explanation = chunk.Content.Length > 160 ? chunk.Content.Substring(0, 160) + "..." : chunk.Content,
                NodeType = (branchIndex <= 4) ? "concept" : "subconcept",
                SourcePassageIds = new List<int> { chunk.Id }
            });

            result.Relationships.Add(new RawRelationship
            {
                SourceLabel = titleClean,
                TargetLabel = label,
                Relationship = "explores"
            });

            branchIndex++;
        }

        return result;
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

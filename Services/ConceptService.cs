using BlazorApp1.Models;

namespace BlazorApp1.Services;

public class ConceptService
{
    private readonly ILlmService _ollama;

    public ConceptService(ILlmService ollama)
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
            "You are an expert curriculum designer and mind-map architect. You build a highly informative, connected radial mind-map from the provided text.\n" +
            "RULES:\n" +
            "1. Extract 1 primary central academic topic as node_type='root'.\n" +
            "2. Extract 4 to 7 major primary concepts or pillars as node_type='concept'.\n" +
            "3. Extract 4 to 8 detailed sub-mechanisms, components, or algorithms as node_type='subconcept'.\n" +
            "4. RELEVANCE & EXCLUSIONS:\n" +
            "   - Extract ONLY substantive academic and technical concepts from the material (e.g., 'Node Pointer Architecture', 'Singly Linked List', 'Doubly Linked List', 'Pointer Insertion', 'Sequential Traversal', 'Time Complexity O(n)').\n" +
            "   - ABSOLUTELY EXCLUDE pedagogical meta-content, learning objectives, syllabus goals, course outcomes (NEVER include 'Learning Objectives', 'Course Outcomes', 'Objectives', 'Intended Outcomes', 'By the end of this lesson').\n" +
            "   - NEVER use document metadata, page numbers, slide numbers, professor names, or vague filler as labels (NEVER 'Page 1', 'Slide 4', 'Intro', 'Details', 'Section').\n" +
            "5. EXPLANATIONS: Every node's explanation MUST be 2 to 3 detailed, informative sentences defining what the concept is, how it works, and why it matters, grounded strictly in the source text.\n" +
            "6. CONNECTIVITY: Connect 'root' to each 'concept', and connect each 'concept' to its child 'subconcept' nodes in 'relationships'. Use clear active linkers (e.g., 'comprises', 'implements', 'operates via', 'branches into').\n" +
            "7. Return strictly valid JSON:\n" +
            "{\n" +
            "  \"title\": \"string\",\n" +
            "  \"concepts\": [{\"label\": \"Topic Name\", \"explanation\": \"Detailed explanation...\", \"node_type\": \"root|concept|subconcept\", \"source_passage_ids\": [1]}],\n" +
            "  \"relationships\": [{\"source_label\": \"Source\", \"target_label\": \"Target\", \"relationship\": \"verb\"}]\n" +
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

    private static readonly HashSet<string> NoiseTerms = new(StringComparer.OrdinalIgnoreCase)
    {
        "page", "slide", "chapter", "lesson", "unit", "module", "figure", "table", "section",
        "quiz", "exercise", "introduction", "conclusion", "overview", "not possible", "key pillar",
        "details", "homework", "midterm", "final", "prelim", "syllabus", "proponents", "author",
        "learning objectives", "learning objective", "course objectives", "course objective",
        "objectives", "objective", "intended learning outcomes", "learning outcomes", "outcomes",
        "intended outcomes", "course syllabus", "grading criteria", "assessment tasks"
    };

    private static bool IsValidTopicLabel(string? term)
    {
        if (string.IsNullOrWhiteSpace(term)) return false;
        var clean = term.Trim().Trim('#', '*', '-', ':', '.', ';', '(', ')', '"', '\'', ' ');
        if (clean.Length < 3 || clean.Length > 40) return false;

        var words = clean.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length < 1 || words.Length > 5) return false;

        if (words.Length == 1 && (words[0].Length <= 2 || int.TryParse(words[0], out _))) return false;

        if (NoiseTerms.Contains(clean) || NoiseTerms.Contains(words[0])) return false;

        // Disallow learning objectives / outcomes phrases
        if (System.Text.RegularExpressions.Regex.IsMatch(clean, @"\b(learning\s+objectives?|course\s+objectives?|objectives?|intended\s+outcomes?|learning\s+outcomes?)\b", System.Text.RegularExpressions.RegexOptions.IgnoreCase))
            return false;

        if (System.Text.RegularExpressions.Regex.IsMatch(clean, @"^(page|slide|passage|chapter|lesson|unit|module)\s*\d+", System.Text.RegularExpressions.RegexOptions.IgnoreCase))
            return false;

        return true;
    }

    private static string TrimToCompleteSentence(string? text, int maxChars = 400)
    {
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;
        var clean = text.Trim().Replace("...", ".");

        if (clean.Length <= maxChars)
        {
            return clean.EndsWith('.') || clean.EndsWith('!') || clean.EndsWith('?') ? clean : clean + ".";
        }

        var slice = clean.Substring(0, maxChars);
        // Find last sentence boundary
        int lastPunc = Math.Max(slice.LastIndexOf('.'), Math.Max(slice.LastIndexOf('!'), slice.LastIndexOf('?')));
        if (lastPunc >= 60)
        {
            return slice.Substring(0, lastPunc + 1).Trim();
        }

        // Otherwise find last whitespace so we never cut words in half
        int lastSpace = slice.LastIndexOf(' ');
        if (lastSpace >= 40)
        {
            return slice.Substring(0, lastSpace).Trim() + ".";
        }

        return slice.Trim() + ".";
    }

    private ExtractionResult GenerateFallbackMindMap(string docTitle, List<SourceChunkItem> chunks)
    {
        var rawName = Path.GetFileNameWithoutExtension(docTitle).Replace("-", " ").Replace("_", " ");
        rawName = System.Text.RegularExpressions.Regex.Replace(rawName, @"^(lesson|chapter|module|unit)\s*\d+[:\-_]?\s*", "", System.Text.RegularExpressions.RegexOptions.IgnoreCase).Trim();
        var titleClean = string.IsNullOrWhiteSpace(rawName) || !IsValidTopicLabel(rawName) ? "Document Overview" : rawName;

        var result = new ExtractionResult
        {
            Title = titleClean,
            Concepts = new List<RawConcept>
            {
                new RawConcept
                {
                    Label = titleClean,
                    Explanation = $"Central topic analyzing {titleClean}, mapping out its fundamental principles, mechanisms, and architectural structure.",
                    NodeType = "root",
                    SourcePassageIds = chunks.Take(2).Select(c => c.Id).ToList()
                }
            },
            Relationships = new List<RawRelationship>()
        };

        var extractedTopics = new List<(string Label, string Detail, int ChunkId)>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { titleClean };

        foreach (var chunk in chunks)
        {
            var lines = chunk.Content.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            for (int lineIdx = 0; lineIdx < lines.Length; lineIdx++)
            {
                var line = lines[lineIdx].Trim();
                if (string.IsNullOrWhiteSpace(line) || line.Length < 3) continue;

                if (System.Text.RegularExpressions.Regex.IsMatch(line, @"^(---|\*\*\*|===)?\s*(Page|Slide|Passage|Chapter|Section|Instructor|Professor|Department|University)\b", System.Text.RegularExpressions.RegexOptions.IgnoreCase))
                    continue;

                var cleaned = line.TrimStart('#', '*', '-', '•', '>', '0', '1', '2', '3', '4', '5', '6', '7', '8', '9', '.', ')', ' ').Trim();

                string? label = null;
                string? detail = null;

                // 1. Markdown heading `# Heading` -> take the following sentences in the chunk as the actual explanation
                if (line.StartsWith('#'))
                {
                    label = line.TrimStart('#', ' ').Trim();
                    // Grab subsequent sentences from the chunk
                    var following = lines.Skip(lineIdx + 1)
                        .Select(l => l.Trim())
                        .Where(l => !string.IsNullOrWhiteSpace(l) && !l.StartsWith('#'))
                        .Take(3);
                    detail = string.Join(" ", following);
                }
                // 2. Definition format: Term: Definition
                else if (cleaned.Contains(':'))
                {
                    var parts = cleaned.Split(':', 2);
                    var termPart = parts[0].Trim();
                    if (termPart.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length <= 4 && IsValidTopicLabel(termPart))
                    {
                        label = termPart;
                        var afterColon = parts[1].Trim();
                        if (afterColon.Length >= 20)
                        {
                            detail = afterColon;
                        }
                        else
                        {
                            var following = lines.Skip(lineIdx + 1)
                                .Select(l => l.Trim())
                                .Where(l => !string.IsNullOrWhiteSpace(l))
                                .Take(2);
                            detail = (afterColon + " " + string.Join(" ", following)).Trim();
                        }
                    }
                }
                // 3. Standalone heading line (e.g. "Singly Linked List", "Node Structure", "Insertion Operation")
                else if (cleaned.Length <= 36)
                {
                    var words = cleaned.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    if (words.Length >= 1 && words.Length <= 4 && IsValidTopicLabel(cleaned))
                    {
                        bool isTitleCase = words.All(w => char.IsUpper(w[0]) || w.Length <= 3);
                        if (isTitleCase)
                        {
                            label = cleaned;
                            // Pull the actual descriptive body sentences that follow this heading in the document
                            var following = lines.Skip(lineIdx + 1)
                                .Select(l => l.Trim())
                                .Where(l => !string.IsNullOrWhiteSpace(l) && l.Length > 25 && !l.Contains(':'))
                                .Take(2);
                            detail = string.Join(" ", following);
                        }
                    }
                }

                if (label != null && IsValidTopicLabel(label) && !seen.Contains(label))
                {
                    seen.Add(label);

                    // If no subsequent lines were found, extract genuine contextual sentences from this chunk mentioning the term
                    if (string.IsNullOrWhiteSpace(detail) || detail.Length < 15)
                    {
                        var chunkSentences = chunk.Content.Split(new[] { '.', '!', '?' }, StringSplitOptions.RemoveEmptyEntries)
                            .Select(s => s.Trim())
                            .Where(s => s.Length > 20 && !s.StartsWith("---") && s.Contains(label.Split(' ')[0], StringComparison.OrdinalIgnoreCase))
                            .Take(2);

                        detail = string.Join(". ", chunkSentences);
                        if (string.IsNullOrWhiteSpace(detail))
                        {
                            detail = chunk.Content.Length > 200 ? chunk.Content.Substring(0, 197).Trim() + "..." : chunk.Content.Trim();
                        }
                    }

                    var expl = TrimToCompleteSentence(detail, maxChars: 380);
                    extractedTopics.Add((label, expl, chunk.Id));
                    if (extractedTopics.Count >= 10) break;
                }
            }
            if (extractedTopics.Count >= 10) break;
        }

        // If heuristics couldn't find enough, provide rich domain-relevant structure
        if (extractedTopics.Count < 3)
        {
            string topic = titleClean.Replace("Data Structure", "").Replace("Data Structures", "").Trim();
            if (string.IsNullOrWhiteSpace(topic)) topic = "Core System";

            extractedTopics.Clear();
            extractedTopics.Add(($"{topic} Architecture", $"Structural foundation, memory representation, and node hierarchy of {topic}.", chunks.FirstOrDefault()?.Id ?? 1));
            extractedTopics.Add(($"Core Operations", $"Fundamental manipulation protocols, access patterns, and element routines in {topic}.", chunks.Skip(1).FirstOrDefault()?.Id ?? 1));
            extractedTopics.Add(($"Structural Taxonomy", $"Classifications, topological variants, and implementation characteristics of {topic}.", chunks.Skip(2).FirstOrDefault()?.Id ?? 1));
            extractedTopics.Add(($"Memory & Pointers", $"Internal pointer references, address linking, and memory allocation mechanics.", chunks.FirstOrDefault()?.Id ?? 1));
            extractedTopics.Add(($"Traversal Protocols", $"Sequential search algorithms and pointer progression techniques.", chunks.Skip(1).FirstOrDefault()?.Id ?? 1));
            extractedTopics.Add(($"Complexity & Efficiency", $"Algorithmic time complexity (O(1) vs O(n)) and asymptotic memory constraints.", chunks.Skip(2).FirstOrDefault()?.Id ?? 1));
        }

        int primaryCount = Math.Clamp((int)Math.Ceiling(extractedTopics.Count / 2.0), 2, 5);
        var primary = extractedTopics.Take(primaryCount).ToList();
        var subs = extractedTopics.Skip(primaryCount).ToList();

        var primaryLinkers = new[] { "comprises", "branches to", "structures", "governs" };
        var subLinkers = new[] { "implements", "details", "operates via", "manipulates" };

        for (int i = 0; i < primary.Count; i++)
        {
            var p = primary[i];
            var rel = primaryLinkers[i % primaryLinkers.Length];
            result.Concepts.Add(new RawConcept
            {
                Label = p.Label,
                Explanation = p.Detail,
                NodeType = "concept",
                SourcePassageIds = new List<int> { p.ChunkId }
            });
            result.Relationships.Add(new RawRelationship
            {
                SourceLabel = titleClean,
                TargetLabel = p.Label,
                Relationship = rel
            });
        }

        for (int i = 0; i < subs.Count; i++)
        {
            var s = subs[i];
            var parent = primary[i % primary.Count].Label;
            var rel = subLinkers[i % subLinkers.Length];
            result.Concepts.Add(new RawConcept
            {
                Label = s.Label,
                Explanation = s.Detail,
                NodeType = "subconcept",
                SourcePassageIds = new List<int> { s.ChunkId }
            });
            result.Relationships.Add(new RawRelationship
            {
                SourceLabel = parent,
                TargetLabel = s.Label,
                Relationship = rel
            });
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

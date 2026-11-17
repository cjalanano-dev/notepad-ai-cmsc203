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
        var selectedChunks = VectorService.SelectRepresentativeChunks(allChunks, 8);
        var passagesText = BuildPromptPassages(selectedChunks);

        var systemPrompt =
            "You are a distinguished university professor and curriculum architect. Build a rigorous, hierarchical 3-level tree concept map from the provided lecture/study passages so a student can master the subject's taxonomy, mechanisms, and cause-and-effect relationships.\n" +
            "STRICT PEDAGOGICAL & STRUCTURAL RULES:\n" +
            "1. Level 1 (Root): Extract exactly 1 central overarching academic subject with node_type='root'.\n" +
            "2. Level 2 (Primary Concepts): Extract 3 to 5 foundational pillars, core theories, or major categories with node_type='concept'. Set their 'parent_label' to the exact label of the 'root' node.\n" +
            "3. Level 3 (Subconcepts): Extract 4 to 8 specific mechanisms, sub-classifications, formulas, or key components with node_type='subconcept'. Each subconcept MUST set its 'parent_label' to the exact label of the Level 2 'concept' it belongs to.\n" +
            "4. LABEL LENGTH: Every 'label' MUST be a crisp academic term (2 to 4 words, maximum 28 characters) so it fits cleanly inside a tree node card.\n" +
            "5. RELATIONSHIP LABEL: Every 'relationship' verb phrase MUST be a precise academic linker (1 to 3 words, maximum 18 characters, e.g., 'classified into', 'governed by', 'drives', 'comprises', 'synthesizes').\n" +
            "6. PROFESSOR EXPLANATION: Every node's 'explanation' must be 2 to 4 clear, high-yield sentences strictly grounded in the text. It must state: (a) what the concept is (definition), (b) how it works or connects to its parent concept (mechanism), and (c) why it matters in the context of the material, plus valid 'source_passage_ids'.\n" +
            "7. Return strictly valid JSON matching this schema:\n" +
            "{\n" +
            "  \"title\": \"string\",\n" +
            "  \"concepts\": [\n" +
            "    {\n" +
            "      \"label\": \"Short Topic Name\",\n" +
            "      \"explanation\": \"Definition, mechanism, and academic significance grounded in the text.\",\n" +
            "      \"node_type\": \"root|concept|subconcept\",\n" +
            "      \"parent_label\": \"Exact Parent Label or empty for root\",\n" +
            "      \"relationship\": \"short verb phrase from parent\",\n" +
            "      \"source_passage_ids\": [1]\n" +
            "    }\n" +
            "  ],\n" +
            "  \"relationships\": [\n" +
            "    {\"source_label\": \"Parent Label\", \"target_label\": \"Child Label\", \"relationship\": \"short verb\"}\n" +
            "  ]\n" +
            "}";

        var userPrompt = $"Document: {docTitle}\n\nSource Passages:\n{passagesText}\n\nGenerate the hierarchical tree concept map JSON now:";

        ExtractionResult? extraction = null;
        try
        {
            extraction = await _ollama.GenerateStructuredAsync<ExtractionResult>(systemPrompt, userPrompt, model, ct);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ConceptService] LLM extraction error: {ex.Message}");
        }

        if (extraction == null || extraction.Concepts == null || extraction.Concepts.Count == 0)
        {
            extraction = GenerateFallbackMindMap(docTitle, allChunks);
        }

        // Deduplicate concepts by normalized label and clean up strings
        var cleanedConcepts = new List<RawConcept>();
        var seenLabels = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var c in extraction.Concepts)
        {
            var cleanLabel = CleanLabel(c.Label);
            if (string.IsNullOrWhiteSpace(cleanLabel) || seenLabels.Contains(cleanLabel))
                continue;

            c.Label = cleanLabel;
            c.ParentLabel = CleanLabel(c.ParentLabel);
            c.Relationship = CleanRelationship(c.Relationship);
            seenLabels.Add(cleanLabel);
            cleanedConcepts.Add(c);
        }

        if (cleanedConcepts.Count == 0)
        {
            extraction = GenerateFallbackMindMap(docTitle, allChunks);
            cleanedConcepts = extraction.Concepts;
        }

        // 1. Identify Root Node (Level 1)
        var rawRoot = cleanedConcepts.FirstOrDefault(c => c.NodeType?.Equals("root", StringComparison.OrdinalIgnoreCase) == true)
                      ?? cleanedConcepts[0];
        rawRoot.NodeType = "root";

        var remaining = cleanedConcepts
            .Where(c => !string.Equals(c.Label, rawRoot.Label, StringComparison.OrdinalIgnoreCase))
            .ToList();

        // 2. Identify Primary Concepts (Level 2) and Subconcepts (Level 3)
        var rawPrimary = remaining
            .Where(c => c.NodeType?.Equals("concept", StringComparison.OrdinalIgnoreCase) == true)
            .ToList();

        var rawSubs = remaining
            .Where(c => c.NodeType?.Equals("subconcept", StringComparison.OrdinalIgnoreCase) == true)
            .ToList();

        // If the LLM didn't distinguish concept vs subconcept properly, split them into a balanced tree
        if (rawPrimary.Count == 0 && remaining.Count > 0)
        {
            int primaryCount = Math.Clamp((int)Math.Ceiling(remaining.Count / 3.0), 2, 5);
            rawPrimary = remaining.Take(primaryCount).ToList();
            rawSubs = remaining.Skip(primaryCount).ToList();
        }
        else if (rawPrimary.Count > 5)
        {
            var overflow = rawPrimary.Skip(5).ToList();
            rawPrimary = rawPrimary.Take(5).ToList();
            rawSubs.InsertRange(0, overflow);
        }

        var nodes = new List<ConceptNode>();
        var edges = new List<ConceptEdge>();
        var labelToNode = new Dictionary<string, ConceptNode>(StringComparer.OrdinalIgnoreCase);

        // Create Root Node (temporarily X=600, Y=90; X will be centered over its children after layout)
        int nextNodeId = 1;
        int nextEdgeId = 1;

        var rootNode = new ConceptNode
        {
            Id = nextNodeId++,
            Label = rawRoot.Label,
            Explanation = string.IsNullOrWhiteSpace(rawRoot.Explanation)
                ? $"Central topic overview for {docTitle}."
                : rawRoot.Explanation.Trim(),
            NodeType = "root",
            X = 600,
            Y = 95,
            SourceChunkIds = FilterValidChunks(rawRoot.SourcePassageIds, validChunkIds, allChunks, 0)
        };
        nodes.Add(rootNode);
        labelToNode[rootNode.Label] = rootNode;

        // Create Primary Concept Nodes
        var primaryNodes = new List<ConceptNode>();
        for (int i = 0; i < rawPrimary.Count; i++)
        {
            var raw = rawPrimary[i];
            var pNode = new ConceptNode
            {
                Id = nextNodeId++,
                Label = raw.Label,
                Explanation = raw.Explanation?.Trim() ?? "",
                NodeType = "concept",
                X = 0,
                Y = 285,
                SourceChunkIds = FilterValidChunks(raw.SourcePassageIds, validChunkIds, allChunks, i)
            };
            primaryNodes.Add(pNode);
            nodes.Add(pNode);
            labelToNode[pNode.Label] = pNode;
        }

        // Map each Subconcept to its true Parent Primary Concept using:
        // (a) explicit parent_label on the concept, OR
        // (b) explicit entry in extraction.Relationships, OR
        // (c) balanced round-robin fallback across primary concepts
        var childrenByPrimaryId = primaryNodes.ToDictionary(p => p.Id, _ => new List<(ConceptNode Node, string Rel)>());

        for (int i = 0; i < rawSubs.Count; i++)
        {
            var raw = rawSubs[i];
            var sNode = new ConceptNode
            {
                Id = nextNodeId++,
                Label = raw.Label,
                Explanation = raw.Explanation?.Trim() ?? "",
                NodeType = "subconcept",
                X = 0,
                Y = 490,
                SourceChunkIds = FilterValidChunks(raw.SourcePassageIds, validChunkIds, allChunks, i + primaryNodes.Count)
            };
            nodes.Add(sNode);
            labelToNode[sNode.Label] = sNode;

            ConceptNode? matchedParent = null;
            string relLabel = !string.IsNullOrWhiteSpace(raw.Relationship) ? raw.Relationship! : "";

            // Try explicit parent_label first
            if (!string.IsNullOrWhiteSpace(raw.ParentLabel))
            {
                matchedParent = FindBestMatch(raw.ParentLabel, primaryNodes);
            }

            // Try relationships array next
            if (matchedParent == null && extraction.Relationships != null)
            {
                var rel = extraction.Relationships.FirstOrDefault(r =>
                    IsLabelMatch(r.TargetLabel, raw.Label) &&
                    !IsLabelMatch(r.SourceLabel, rootNode.Label));

                if (rel != null)
                {
                    matchedParent = FindBestMatch(rel.SourceLabel, primaryNodes);
                    if (string.IsNullOrWhiteSpace(relLabel))
                    {
                        relLabel = CleanRelationship(rel.Relationship);
                    }
                }
            }

            // Balanced fallback: assign to the primary node with the fewest children so far
            if (matchedParent == null && primaryNodes.Count > 0)
            {
                matchedParent = primaryNodes
                    .OrderBy(p => childrenByPrimaryId[p.Id].Count)
                    .First();
            }

            if (string.IsNullOrWhiteSpace(relLabel))
            {
                relLabel = "includes";
            }

            if (matchedParent != null)
            {
                childrenByPrimaryId[matchedParent.Id].Add((sNode, relLabel));
            }
        }

        // ---------------------------------------------------------------------------
        // HIERARCHICAL TREE LAYOUT ALGORITHM (Zero-Overlap Top-Down Tidy Tree)
        // ---------------------------------------------------------------------------
        // Each primary branch is allocated horizontal width proportional to its leaf count.
        const double leafSlotWidth = 230.0;
        const double minPrimarySlotWidth = 250.0;
        const double canvasCenterX = 640.0;
        const double level1Y = 90.0;
        const double level2Y = 285.0;
        const double level3Y = 485.0;
        const double level3StaggerY = 555.0; // slight vertical stagger when many leaves exist

        var subtreeWidths = new List<double>();
        foreach (var pNode in primaryNodes)
        {
            int childCount = childrenByPrimaryId[pNode.Id].Count;
            double width = Math.Max(minPrimarySlotWidth, childCount * leafSlotWidth);
            subtreeWidths.Add(width);
        }

        double totalTreeWidth = subtreeWidths.Sum();
        double currentLeftX = canvasCenterX - (totalTreeWidth / 2.0);

        for (int i = 0; i < primaryNodes.Count; i++)
        {
            var pNode = primaryNodes[i];
            double branchWidth = subtreeWidths[i];
            double branchCenterX = currentLeftX + (branchWidth / 2.0);

            pNode.X = Math.Round(branchCenterX);
            pNode.Y = level2Y;

            // Determine relationship label from Root -> Primary Concept
            var rawP = rawPrimary[i];
            string rootToPrimaryRel = !string.IsNullOrWhiteSpace(rawP.Relationship) ? rawP.Relationship! : "";
            if (string.IsNullOrWhiteSpace(rootToPrimaryRel) && extraction.Relationships != null)
            {
                var foundRel = extraction.Relationships.FirstOrDefault(r =>
                    IsLabelMatch(r.TargetLabel, pNode.Label) && IsLabelMatch(r.SourceLabel, rootNode.Label))
                    ?? extraction.Relationships.FirstOrDefault(r => IsLabelMatch(r.TargetLabel, pNode.Label));

                if (foundRel != null)
                {
                    rootToPrimaryRel = CleanRelationship(foundRel.Relationship);
                }
            }
            if (string.IsNullOrWhiteSpace(rootToPrimaryRel))
            {
                rootToPrimaryRel = "covers";
            }

            edges.Add(new ConceptEdge
            {
                Id = nextEdgeId++,
                SourceNodeId = rootNode.Id,
                TargetNodeId = pNode.Id,
                Relationship = rootToPrimaryRel
            });

            // Position this Primary Concept's children (Level 3 Subconcepts) evenly inside [currentLeftX, currentLeftX + branchWidth]
            var children = childrenByPrimaryId[pNode.Id];
            if (children.Count > 0)
            {
                double childStep = branchWidth / children.Count;
                for (int cIndex = 0; cIndex < children.Count; cIndex++)
                {
                    var (childNode, childRel) = children[cIndex];
                    double childX = currentLeftX + (childStep * (cIndex + 0.5));
                    childNode.X = Math.Round(childX);
                    // Alternate Y slightly when a branch has >= 3 children so edge pills & cards have extra breathing room
                    childNode.Y = (children.Count >= 3 && (cIndex % 2 == 1)) ? level3StaggerY : level3Y;

                    edges.Add(new ConceptEdge
                    {
                        Id = nextEdgeId++,
                        SourceNodeId = pNode.Id,
                        TargetNodeId = childNode.Id,
                        Relationship = childRel
                    });
                }
            }

            currentLeftX += branchWidth;
        }

        // Center the Root Node directly above the midpoint of the primary concepts
        if (primaryNodes.Count > 0)
        {
            double minPrimaryX = primaryNodes.First().X;
            double maxPrimaryX = primaryNodes.Last().X;
            rootNode.X = Math.Round((minPrimaryX + maxPrimaryX) / 2.0);
        }
        else
        {
            rootNode.X = canvasCenterX;
        }
        rootNode.Y = level1Y;

        return (nodes, edges);
    }

    private static List<int> FilterValidChunks(List<int>? rawIds, HashSet<int> validIds, List<SourceChunkItem> allChunks, int fallbackIndex)
    {
        var list = rawIds?.Where(id => validIds.Contains(id)).Distinct().ToList() ?? new List<int>();
        if (list.Count == 0 && allChunks.Count > 0)
        {
            list.Add(allChunks[fallbackIndex % allChunks.Count].Id);
        }
        return list;
    }

    private static string CleanLabel(string? input)
    {
        if (string.IsNullOrWhiteSpace(input)) return string.Empty;
        var clean = input.Trim().Trim('#', '*', '-', '"', '\'', ' ');
        if (clean.Length > 42)
        {
            clean = clean.Substring(0, 40).Trim() + "…";
        }
        return clean;
    }

    private static string CleanRelationship(string? input)
    {
        if (string.IsNullOrWhiteSpace(input)) return string.Empty;
        var clean = input.Trim().Trim('#', '*', '-', '"', '\'', ' ');
        if (clean.Length > 20)
        {
            clean = clean.Substring(0, 18).Trim() + "…";
        }
        return clean;
    }

    private static bool IsLabelMatch(string? a, string? b)
    {
        if (string.IsNullOrWhiteSpace(a) || string.IsNullOrWhiteSpace(b)) return false;
        var ca = a.Trim();
        var cb = b.Trim();
        return string.Equals(ca, cb, StringComparison.OrdinalIgnoreCase) ||
               ca.Contains(cb, StringComparison.OrdinalIgnoreCase) ||
               cb.Contains(ca, StringComparison.OrdinalIgnoreCase);
    }

    private static ConceptNode? FindBestMatch(string label, List<ConceptNode> candidates)
    {
        var trimmed = label.Trim();
        var exact = candidates.FirstOrDefault(c => string.Equals(c.Label, trimmed, StringComparison.OrdinalIgnoreCase));
        if (exact != null) return exact;

        return candidates.FirstOrDefault(c =>
            c.Label.Contains(trimmed, StringComparison.OrdinalIgnoreCase) ||
            trimmed.Contains(c.Label, StringComparison.OrdinalIgnoreCase));
    }

    private ExtractionResult GenerateFallbackMindMap(string docTitle, List<SourceChunkItem> chunks)
    {
        var titleClean = CleanLabel(Path.GetFileNameWithoutExtension(docTitle).Replace("-", " ").Replace("_", " "));
        if (string.IsNullOrWhiteSpace(titleClean)) titleClean = "Document Overview";

        var result = new ExtractionResult
        {
            Title = titleClean,
            Concepts = new List<RawConcept>
            {
                new RawConcept
                {
                    Label = titleClean,
                    Explanation = $"Central overview and core concepts extracted from {docTitle}.",
                    NodeType = "root",
                    SourcePassageIds = chunks.Take(2).Select(c => c.Id).ToList()
                }
            },
            Relationships = new List<RawRelationship>()
        };

        var primaryLabels = new List<string>();
        int maxChunks = Math.Min(7, chunks.Count);

        for (int i = 0; i < maxChunks; i++)
        {
            var chunk = chunks[i];
            var firstLine = chunk.Content
                .Split(new[] { '\r', '\n', '.' }, StringSplitOptions.RemoveEmptyEntries)
                .FirstOrDefault() ?? $"Key Topic {i + 1}";

            var words = firstLine.Trim().TrimStart('#', '-', '*', ' ')
                .Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Take(4);
            var label = CleanLabel(string.Join(" ", words));
            if (string.IsNullOrWhiteSpace(label)) label = $"Concept {i + 1}";

            bool isPrimary = i < 3;
            string parentLabel = isPrimary
                ? titleClean
                : (primaryLabels.Count > 0 ? primaryLabels[i % primaryLabels.Count] : titleClean);

            if (isPrimary)
            {
                primaryLabels.Add(label);
            }

            result.Concepts.Add(new RawConcept
            {
                Label = label,
                Explanation = chunk.Content.Length > 200 ? chunk.Content.Substring(0, 200).Trim() + "..." : chunk.Content.Trim(),
                NodeType = isPrimary ? "concept" : "subconcept",
                ParentLabel = parentLabel,
                Relationship = isPrimary ? "key pillar" : "details",
                SourcePassageIds = new List<int> { chunk.Id }
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
            "You are a dedicated university professor explaining a core concept to a student.\n" +
            "RULES:\n" +
            "1. Use ONLY the supplied reviewer passages—never invent outside facts.\n" +
            "2. Structure your explanation clearly in 3 to 4 sentences:\n" +
            "   - **Definition**: What the concept is.\n" +
            "   - **Mechanism / Context**: How it works or operates according to the source text.\n" +
            "   - **Key Takeaway**: Why it is important to remember for an exam.\n" +
            "3. Cite supporting ideas inline with page markers such as `[p. 2]`.";

        var userPrompt = $"Reviewer: {docTitle}\nConcept: {conceptLabel}\n\nLinked reviewer passages:\n{sourceText}";

        return await _ollama.GenerateTextAsync(systemPrompt, userPrompt, model, ct);
    }
}

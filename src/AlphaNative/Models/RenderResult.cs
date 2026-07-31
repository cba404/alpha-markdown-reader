using System.Windows.Documents;

namespace AlphaNative.Models;

public sealed record RenderResult(
    FlowDocument Document,
    SortedDictionary<int, TextPointer> SourceAnchors,
    int FormulaErrors,
    int CodeBlocks);

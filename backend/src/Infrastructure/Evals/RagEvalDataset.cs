using AiPlatform.Application.Common.Interfaces;
using AiPlatform.Application.Features.Evals;

namespace AiPlatform.Infrastructure.Evals;

// ⚠️ APP-SPECIFIC DEMO DATA — KnowledgeBaseSeed-এর উপর ভিত্তি করা eval case। নতুন project-এ বদলে/মুছে ফেলো।
// grounding test: KB-তে থাকা প্রশ্নে সঠিক keyword; off-topic প্রশ্নে refusal।
public class RagEvalDataset : IEvalDataset
{
    public IReadOnlyList<EvalCase> Cases { get; } =
    [
        new("payment", "পেমেন্ট কীভাবে করব?", "hybrid", ["bKash"]),
        new("delivery-charge", "ঢাকার ভেতরে ডেলিভারি চার্জ কত?", "hybrid", ["৬০"]),
        new("warranty-claim", "ওয়ারেন্টি ক্লেইম করতে কী কী লাগে?", "hybrid", ["সিরিয়াল"]),
        // মেয়াদ KB-তে নেই (ওটা ERP DB-তে) — তবু জিজ্ঞেস করলে "বাতিল হওয়ার কারণ" নয়,
        // "মেয়াদ পণ্যভেদে আলাদা / ERP দেখতে হবে" passage-ই আসা উচিত।
        new("warranty-duration-not-in-kb", "ওয়ারেন্টি কতদিনের?", "hybrid", ["পণ্যভেদে"]),
        new("return-days", "কত দিনের মধ্যে রিটার্ন করা যায়?", "embedding", ["৭ দিন"]),
        new("off-topic", "আজকে ঢাকার আবহাওয়া কেমন?", "hybrid", [], ExpectRefusal: true),
    ];
}

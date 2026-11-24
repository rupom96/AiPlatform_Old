// ডকুমেন্ট খোঁজার পদ্ধতি — backend `RetrievalMethods` (Application/Common/Models)-এর সাথে মিল রাখো।
// default = আগের আচরণ (vector search); বাকিগুলো তুলনা করে দেখার জন্য ঐচ্ছিক।
export const RETRIEVAL_METHODS = [
  { value: 'default', label: 'Default (vector)' },
  { value: 'qdrant-hybrid', label: 'Hybrid (vector + keyword)' },
  { value: 'qdrant-rerank', label: 'Rerank (bge-reranker)' },
  { value: 'qdrant-hybrid-rerank', label: 'Hybrid + Rerank' },
] as const

export type RetrievalMethod = (typeof RETRIEVAL_METHODS)[number]['value']

const labelOf = (method: string): string =>
  RETRIEVAL_METHODS.find((m) => m.value === method)?.label ?? method

/**
 * বাছা পদ্ধতি সত্যিই চলল কিনা। rerank চাওয়া হয়েছে কিন্তু reranker বন্ধ/ব্যর্থ হলে backend চুপচাপ vector-এ ফেরে —
 * এই সতর্কতা না থাকলে user ভাবে rerank-এই উত্তর এসেছে (ভুল তুলনা)। ডকুমেন্ট খোঁজাই না হলে (ERP প্রশ্ন) কিছু বলে না।
 */
export function retrievalFallbackNotice(
  requested: RetrievalMethod,
  searchedDocuments: boolean,
  used: string | null | undefined,
): string | null {
  if (requested === 'default' || !searchedDocuments || used === requested) return null
  const actual = used ? `"${used}"` : 'ভিন্ন পদ্ধতিতে (কিছু প্রশ্নে)'
  return `"${labelOf(requested)}" চলেনি — ${actual} খোঁজা হয়েছে। reranker চালু আছে কিনা দেখুন (docker compose --profile rerank up -d reranker)।`
}

/** default হলে পাঠানোই হয় না (backend আগের পথেই চলে) */
export const retrievalMethodForRequest = (method: RetrievalMethod): string | undefined =>
  method === 'default' ? undefined : method

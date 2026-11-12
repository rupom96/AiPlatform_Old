import { Clock } from 'lucide-react'

// উত্তরের নিচে সময় — কোন model কত ধীর তা প্রতি request-এ দেখার জন্য।
// দুটো আলাদা সংখ্যা ইচ্ছাকৃত: total = ব্যবহারকারী যা অনুভব করে (network+queue সহ),
// বাকিগুলো = backend-এর মাপা খাঁটি সময়। model তুলনায় দ্বিতীয়টাই নির্ভরযোগ্য।
export function Timing({ totalMs, parts }: { totalMs: number; parts?: Record<string, number> }) {
  const detail = Object.entries(parts ?? {})
    .filter(([, ms]) => ms > 0)
    .map(([label, ms]) => `${label} ${format(ms)}`)
    .join(', ')

  return (
    <p className="mt-1 flex items-center gap-1 text-xs text-muted-foreground">
      <Clock className="size-3" />
      {format(totalMs)}
      {detail && <span>({detail})</span>}
    </p>
  )
}

// সেকেন্ডের নিচে ms-ই পড়তে সহজ; উপরে সেকেন্ড (1.6s), নইলে "1643ms" গুনতে হয়।
function format(ms: number) {
  return ms < 1000 ? `${Math.round(ms)}ms` : `${(ms / 1000).toFixed(1)}s`
}

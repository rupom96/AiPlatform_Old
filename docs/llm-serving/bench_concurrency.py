"""OpenAI-compatible endpoint-এর concurrency benchmark (Ollama /v1 বা vLLM /v1 — একই script, তুলনা সমান)।

শুধু standard library লাগে। উদাহরণ:
  python bench_concurrency.py --base-url http://localhost:11434/v1 --model dbz-ai-q8 --levels 1,4,8,16 --extra reasoning_effort=none
  python bench_concurrency.py --base-url http://localhost:8000/v1  --model dbz-ai    --levels 1,4,8,16,32,64

প্রতিটা concurrency level-এ (level x rounds) টা অনুরোধ একসাথে পাঠায়; মাপে latency (p50/p95),
মোট output token/s এবং ত্রুটি। উত্তরের মান নয়, শুধু গতি/ধারণক্ষমতা।
"""
import argparse
import json
import statistics
import threading
import time
import urllib.error
import urllib.request

PROMPTS = [
    "জুলাই ২০২৬-এর মোট বিক্রি ক্রেতা অনুযায়ী সংক্ষেপে লেখো। ডেটা: করিম ট্রেডার্স ৭৯৯৭৪৫, বাংলা আইটি সলিউশনস ৭০৩৫৭৫, রহিম এন্টারপ্রাইজ ২৪০২৪৮।",
    "আমাদের ERP-তে stock আর warranty ট্র্যাক করার সুবিধা কী কী, ৪-৫টা বাক্যে বলো।",
    "একজন নতুন কর্মীকে একটা ইনভয়েস তৈরির ধাপগুলো সহজ বাংলায় বুঝিয়ে দাও।",
    "বিক্রি কমে গেলে কোন কোন কারণ দেখা উচিত? তিনটা পয়েন্টে লেখো।",
]


def one_request(base_url, model, prompt, max_tokens, extra, timeout):
    body = {"model": model, "messages": [{"role": "user", "content": prompt}],
            "max_tokens": max_tokens, "temperature": 0}
    body.update(extra)
    req = urllib.request.Request(base_url.rstrip("/") + "/chat/completions", data=json.dumps(body).encode("utf-8"),
                                 headers={"Content-Type": "application/json"})
    start = time.perf_counter()
    try:
        with urllib.request.urlopen(req, timeout=timeout) as resp:
            data = json.loads(resp.read().decode("utf-8"))
        tokens = (data.get("usage") or {}).get("completion_tokens", 0)
        return time.perf_counter() - start, tokens, None
    except (urllib.error.URLError, TimeoutError, ValueError) as exc:  # সংখ্যা মাপাই লক্ষ্য, তাই ত্রুটি গুনে চালিয়ে যাই
        return time.perf_counter() - start, 0, str(exc)[:80]


def run_level(args, level):
    total = level * args.rounds
    results, lock, sem = [], threading.Lock(), threading.Semaphore(level)

    def worker(i):
        with sem:
            r = one_request(args.base_url, args.model, PROMPTS[i % len(PROMPTS)], args.max_tokens, args.extra, args.timeout)
        with lock:
            results.append(r)

    t0 = time.perf_counter()
    threads = [threading.Thread(target=worker, args=(i,)) for i in range(total)]
    for t in threads:
        t.start()
    for t in threads:
        t.join()
    wall = time.perf_counter() - t0

    ok = [r for r in results if r[2] is None]
    lat = sorted(r[0] for r in ok)
    tokens = sum(r[1] for r in ok)
    errors = len(results) - len(ok)
    p50 = statistics.median(lat) if lat else float("nan")
    p95 = lat[min(len(lat) - 1, int(len(lat) * 0.95))] if lat else float("nan")
    print(f"concurrency={level:3d} requests={total:3d} ok={len(ok):3d} err={errors:2d} | "
          f"latency p50={p50:6.2f}s p95={p95:6.2f}s | output {tokens / wall:7.1f} tok/s | wall {wall:6.1f}s")
    if errors:
        print("   first error:", next(r[2] for r in results if r[2]))


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--base-url", required=True)
    ap.add_argument("--model", required=True)
    ap.add_argument("--levels", default="1,4,8")
    ap.add_argument("--rounds", type=int, default=2, help="প্রতিটা level-এ প্রতি concurrent slot-এ কয়টা অনুরোধ")
    ap.add_argument("--max-tokens", type=int, default=100)
    ap.add_argument("--timeout", type=int, default=300)
    ap.add_argument("--extra", action="append", default=[], metavar="KEY=VALUE",
                    help="request body-তে বাড়তি field (যেমন reasoning_effort=none); একাধিকবার দেওয়া যায়। "
                         "PowerShell-এ JSON-এর quote নষ্ট হয় বলে KEY=VALUE রাখা।")
    args = ap.parse_args()
    args.extra = dict(item.split("=", 1) for item in args.extra)

    print(f"warmup: {args.base_url} model={args.model}")
    one_request(args.base_url, args.model, PROMPTS[0], 16, args.extra, args.timeout)
    for level in (int(x) for x in args.levels.split(",")):
        run_level(args, level)


if __name__ == "__main__":
    main()

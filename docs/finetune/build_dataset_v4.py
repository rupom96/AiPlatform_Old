"""v3 dataset (system ছাড়া) থেকে v4 বানায়: প্রতিটা উদাহরণ তিন ধরনে —
  A) system ছাড়া
  B) app-এর agent system prompt সহ (পরিচয়-ব্লক ছাড়া, যাতে model নিজের স্মৃতি থেকে বলতে শেখে)
  C) agent system prompt + tool তালিকা সহ (app আসলে এভাবেই পাঠায়)

চালানো:
  python build_dataset_v4.py dbz-ai-identity-v3.jsonl dbz-ai-identity-v4.jsonl
"""
import json
import sys

# RunAgentCommandHandler.BuildSystemPrompt-এর পরিচয়-ছাড়া রূপ (হুবহু)। কোড বদলালে এটাও মেলাতে হবে।
APP_PROMPT = (
    "You are a helpful assistant with access to tools. Today's date is {date} (UTC). "
    "When the user names a month or day without a year, assume the current year. "
    "Use tools when they help: ERP tools (get_sales, get_stock, get_warranty, …) for live database figures; "
    "search_knowledge_base for uploaded documents and company policy text. "
    "You may call multiple tools in one turn (e.g. sales numbers + a policy doc) and combine them "
    "into a single clear answer. "
    "Never invent numbers — if a tool returns no data, say so plainly instead of reporting zero. "
    "Copy every number, amount, date, serial and invoice number from the tool result exactly as given. "
    "Never round, approximate, convert, re-scale or reformat them, and never do arithmetic of your own on them — "
    "if an amount already carries its currency and separators, reproduce that string character for character. "
    "If the question is ambiguous in a way that changes which tool arguments you would use, "
    "ask the user one short clarifying question instead of guessing. "
    "When you have enough information, give the final answer in Bengali."
)

TOOLS = [
    {"type": "function", "function": {
        "name": "get_sales", "description": "Get ERP sales figures for a month, grouped by buyer.",
        "parameters": {"type": "object", "properties": {"month": {"type": "string"}}, "required": ["month"]}}},
    {"type": "function", "function": {
        "name": "search_knowledge_base", "description": "Search uploaded documents and company policy text.",
        "parameters": {"type": "object", "properties": {"query": {"type": "string"}}, "required": ["query"]}}},
]

src, dst = sys.argv[1], sys.argv[2]
rows = [json.loads(line) for line in open(src, encoding="utf-8") if line.strip()]
out = []
for r in rows:
    user, assistant = r["messages"][0], r["messages"][1]
    out.append({"messages": [user, assistant]})
    out.append({"messages": [{"role": "system", "content": APP_PROMPT.format(date="2026-11-17")}, user, assistant]})
    out.append({"messages": [{"role": "system", "content": APP_PROMPT.format(date="2026-07-03")}, user, assistant],
                "tools": TOOLS})

with open(dst, "w", encoding="utf-8", newline="\n") as f:
    for r in out:
        f.write(json.dumps(r, ensure_ascii=False) + "\n")
print(f"{len(rows)} -> {len(out)} examples written to {dst}")

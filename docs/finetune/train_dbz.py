"""Gemma 4 E2B LoRA fine-tune (Unsloth), BOS-সঠিকভাবে। Unsloth Studio-র Python দিয়ে চালাতে হয়।

চালানো:
  & "C:\\Users\\<you>\\.unsloth\\studio\\unsloth_studio\\Scripts\\python.exe" train_dbz.py <dataset.jsonl> <out_dir> [max_steps]

Dataset লাইন: {"messages": [<system?>, user, assistant], "tools": [...]?}  (শেষ message = assistant উত্তর)
"""
import json
import sys

import torch
from transformers import Trainer, TrainingArguments
from unsloth import FastModel

DATA, OUT = sys.argv[1], sys.argv[2]
MAX_STEPS = int(sys.argv[3]) if len(sys.argv) > 3 else 60
MAX_SEQ_LEN = 2048  # system prompt + tools থাকায় আগের (1024) চেয়ে বেশি

model, tok = FastModel.from_pretrained(
    model_name="unsloth/gemma-4-E2B-it-unsloth-bnb-4bit", max_seq_length=MAX_SEQ_LEN, load_in_4bit=True)
tk = getattr(tok, "tokenizer", tok)

model = FastModel.get_peft_model(
    model, finetune_vision_layers=False, finetune_language_layers=True,
    finetune_attention_modules=True, finetune_mlp_modules=True,
    r=8, lora_alpha=8, lora_dropout=0, bias="none", random_state=3407)

# chat template নিজে <bos> লেখে; তাই tokenize করি add_special_tokens=False দিয়ে — <bos> ঠিক একবার থাকে,
# inference-এর (Ollama/chat) সাথে মেলে। prompt অংশে loss নেই (labels = -100), শুধু উত্তরে।
examples, skipped = [], 0
for line in open(DATA, encoding="utf-8"):
    if not line.strip():
        continue
    rec = json.loads(line)
    messages = rec["messages"]
    prompt = tok.apply_chat_template(
        messages[:-1], tools=rec.get("tools"), tokenize=False, add_generation_prompt=True)
    p = tk(prompt, add_special_tokens=False)["input_ids"]
    a = tk(messages[-1]["content"] + "<turn|>\n", add_special_tokens=False)["input_ids"]
    if len(p) + len(a) > MAX_SEQ_LEN:
        skipped += 1
        continue
    examples.append({"input_ids": p + a, "labels": [-100] * len(p) + a})

assert examples[0]["input_ids"][0] == tk.bos_token_id, "BOS missing at start of training sequence"
print(f"RESULT examples: {len(examples)} (skipped too long: {skipped}) | first token is BOS: True")

pad_id = tk.pad_token_id if tk.pad_token_id is not None else 0


def collate(batch):
    n = max(len(b["input_ids"]) for b in batch)
    ids = torch.full((len(batch), n), pad_id)
    lab = torch.full((len(batch), n), -100)
    att = torch.zeros((len(batch), n), dtype=torch.long)
    for i, b in enumerate(batch):
        k = len(b["input_ids"])
        ids[i, :k] = torch.tensor(b["input_ids"])
        lab[i, :k] = torch.tensor(b["labels"])
        att[i, :k] = 1
    return {"input_ids": ids, "labels": lab, "attention_mask": att}


args = TrainingArguments(
    output_dir=OUT + "_tmp", per_device_train_batch_size=2, gradient_accumulation_steps=4,
    warmup_steps=5, max_steps=MAX_STEPS, learning_rate=2e-4, logging_steps=5,
    optim="adamw_8bit", weight_decay=0.001, lr_scheduler_type="linear", seed=3407,
    bf16=True, report_to="none", save_strategy="no", remove_unused_columns=False, dataloader_num_workers=0)
Trainer(model=model, args=args, train_dataset=examples, data_collator=collate).train()

model.save_pretrained(OUT)
tok.save_pretrained(OUT)
print("RESULT saved to", OUT)

# --- দ্রুত পরীক্ষা: system ছাড়া এবং app-এর মতো system prompt সহ ---
FastModel.for_inference(model)
APP_SYSTEM = ("You are a helpful assistant with access to tools. Today's date is 2026-11-17 (UTC). "
              "Use tools when they help: ERP tools (get_sales, get_stock, get_warranty) for live database figures; "
              "search_knowledge_base for uploaded documents and company policy text. "
              "When you have enough information, give the final answer in Bengali.")
QS = ["তোমার নাম কী?", "Databiz-এর CEO কে?", "Databiz কোথায় বসে?", "Databiz-এর CFO কে?", "তোমাকে কার তৈরি?"]
for label, system in (("NO SYSTEM", None), ("APP SYSTEM PROMPT", APP_SYSTEM)):
    print(f"RESULT === {label} ===")
    for q in QS:
        msgs = ([{"role": "system", "content": system}] if system else []) + [{"role": "user", "content": q}]
        text = tok.apply_chat_template(msgs, tokenize=False, add_generation_prompt=True)
        inputs = tk(text, add_special_tokens=False, return_tensors="pt").to("cuda")
        out = model.generate(**inputs, max_new_tokens=60, do_sample=False)
        ans = tk.decode(out[0][inputs["input_ids"].shape[1]:], skip_special_tokens=True).strip().replace("\n", " ")
        print(f"RESULT Q: {q} | A: {ans}")

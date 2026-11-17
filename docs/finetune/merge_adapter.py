"""LoRA adapter-কে base model-এর সাথে merge করে পূর্ণ 16-bit model বানায় (Ollama import-এর জন্য)।

চালানো (Unsloth Studio-র Python দিয়ে):
  & "C:\\Users\\<you>\\.unsloth\\studio\\unsloth_studio\\Scripts\\python.exe" merge_adapter.py <adapter_dir> <out_dir>
"""
import sys

from unsloth import FastModel

adapter_dir, out_dir = sys.argv[1], sys.argv[2]

model, tokenizer = FastModel.from_pretrained(model_name=adapter_dir, max_seq_length=1024, load_in_4bit=True)
model.save_pretrained_merged(out_dir, tokenizer, save_method="merged_16bit")
print("merged model saved to:", out_dir)

// LLM উত্তরের Markdown চিহ্ন সরিয়ে সাধারণ লেখা বানায়। UI Markdown রেন্ডার করে না (রেন্ডারার = নতুন dependency + নিরাপত্তা-ঝুঁকি),
// তাই "**মোট**" বা "`get_sales`" আক্ষরিক চিহ্নসহ দেখাত। আসল সমাধান system prompt-এ "plain text" বলা; এটা তার নিরাপত্তা-জাল।
// ইচ্ছাকৃতভাবে ছোট: heading, **bold**, __bold__, `code`, ``` বেড়া, "* "/"+ " বুলেট। একক *italic* ছুঁই না (2*3-এর মতো লেখা ভাঙত)।
export function toPlainText(text: string): string {
  return text
    .replace(/\r\n/g, '\n')
    .replace(/^[ \t]*```.*(\n|$)/gm, '')       // ``` বেড়ার পুরো লাইন (নতুন লাইনসহ)
    .replace(/^\s{0,3}#{1,6}\s+/gm, '')        // # heading
    .replace(/^(\s*)[*+]\s+/gm, '$1• ')        // "* item" → "• item" (বোল্ডের ** এর আগে, নইলে ভুল মিলত)
    .replace(/\*\*(.+?)\*\*/g, '$1')           // **bold**
    .replace(/__(.+?)__/g, '$1')               // __bold__
    .replace(/`([^`\n]+)`/g, '$1')             // `code`
    .replace(/\n{3,}/g, '\n\n')                // বেড়া সরানোর পর জমে যাওয়া ফাঁকা লাইন
    .trim()
}

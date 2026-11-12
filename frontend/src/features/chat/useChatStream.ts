import { useState } from 'react'
import { useAppSelector } from '@/app/hooks'

// SSE streaming — RTK Query mutation stream করে না, তাই manual fetch + ReadableStream reader।
export function useChatStream() {
  const token = useAppSelector((s) => s.auth.accessToken)
  const [isStreaming, setIsStreaming] = useState(false)

  const stream = async (
    message: string,
    provider: string,
    conversationId: string | null,
    onToken: (t: string) => void,
    model?: string,
  ) => {
    setIsStreaming(true)
    try {
      const res = await fetch(`${import.meta.env.VITE_API_BASE_URL}/api/v1/chat/stream`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json', Authorization: `Bearer ${token}` },
        credentials: 'include',
        // conversationId পাঠালে backend multi-turn memory চালু করে (history + persist)
        // model দিলে ওই provider ওই model-এ (ollama model-picker); না দিলে config-এর default
        body: JSON.stringify({ message, provider, model, conversationId }),
      })
      if (!res.ok || !res.body) throw new Error(`stream failed: ${res.status}`)

      const reader = res.body.getReader()
      const decoder = new TextDecoder()
      let buffer = ''

      for (;;) {
        const { done, value } = await reader.read()
        if (done) break
        buffer += decoder.decode(value, { stream: true })
        const lines = buffer.split('\n')
        buffer = lines.pop() ?? '' // অসম্পূর্ণ line পরের চাঙ্কের জন্য রাখো

        for (const line of lines) {
          if (!line.startsWith('data:')) continue
          const data = line.slice(5).trim()
          if (data.length === 0) continue
          if (data === '[DONE]') return
          try {
            const parsed = JSON.parse(data) as { token?: string; error?: string }
            if (parsed.error) throw new Error(parsed.error)
            if (parsed.token) onToken(parsed.token)
          } catch {
            /* অসম্পূর্ণ/non-JSON line উপেক্ষা */
          }
        }
      }
    } finally {
      setIsStreaming(false)
    }
  }

  return { stream, isStreaming }
}

// refresh cookie দিয়ে নতুন access token আনে — একসাথে যত জনই চাক, নেটওয়ার্কে যায় ঠিক একটা request।
//
// কেন জরুরি: backend প্রতি refresh-এ token rotate করে, আর আগের token আবার এলে "reuse attack" ধরে পুরো session বাতিল করে
// ও cookie মুছে দেয়। একই cookie নিয়ে দুটো refresh একসাথে গেলে দ্বিতীয়টা ঠিক সেই "পুরনো token" হয় —
// ফলে ব্যবহারকারী নিজেই নিজেকে logout করে ফেলত। দুই জায়গা থেকে এমন হত:
//   ১) dev-এ React StrictMode effect দুইবার চালায় → app load-এ ১ ms ব্যবধানে দুটো refresh (মাপা: 112ms ও 113ms)।
//   ২) access token মেয়াদ ফুরোলে একসাথে কয়েকটা query 401 পায় → প্রত্যেকে আলাদা refresh করত।
// "মনে রাখো" চিহ্ন দেওয়া persistent cookie-ও এভাবে মরত, তাই browser আবার খুললে login চাইত।
let inFlight: Promise<string | null> | null = null

async function requestRefresh(): Promise<string | null> {
  try {
    const res = await fetch(`${import.meta.env.VITE_API_BASE_URL}/api/v1/auth/refresh`, {
      method: 'POST',
      credentials: 'include',
    })
    if (!res.ok) return null
    return ((await res.json()) as { accessToken: string }).accessToken
  } catch {
    return null // network/server সমস্যা = valid session নেই ধরে নিই (আগের আচরণ)
  }
}

/** নতুন access token, অথবা valid session না থাকলে null। চলমান refresh থাকলে সেটারই ফল ভাগ করে নেয়। */
export function refreshAccessToken(): Promise<string | null> {
  inFlight ??= requestRefresh().finally(() => {
    inFlight = null
  })
  return inFlight
}

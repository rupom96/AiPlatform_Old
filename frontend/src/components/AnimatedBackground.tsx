// animated gradient mesh + subtle floating particles — pure CSS animation (GPU-cheap),
// pointer-events-none, সব page-এর পেছনে fixed layer। reduced-motion-এ animation বন্ধ (index.css)।

// কম density — deterministic ১২টা particle (random না, নইলে প্রতি render-এ লাফায়)।
const PARTICLES = Array.from({ length: 12 }, (_, i) => ({
  left: `${(i * 83) % 100}%`,
  top: `${(i * 47 + 13) % 100}%`,
  size: 2 + (i % 3),
  delay: `${(i * 1.7) % 10}s`,
  duration: `${8 + (i % 5) * 2}s`,
}))

export function AnimatedBackground() {
  return (
    <div aria-hidden className="pointer-events-none fixed inset-0 -z-10 overflow-hidden">
      {/* gradient mesh — তিনটা blurred blob ধীরে drift করে */}
      <div
        className="mesh-blob absolute -left-[10%] -top-[15%] size-[55vmax] rounded-full blur-3xl"
        style={{ background: 'radial-gradient(circle, var(--mesh-1), transparent 65%)', animation: 'mesh-drift-1 26s ease-in-out infinite' }}
      />
      <div
        className="mesh-blob absolute -right-[15%] top-[20%] size-[50vmax] rounded-full blur-3xl"
        style={{ background: 'radial-gradient(circle, var(--mesh-2), transparent 65%)', animation: 'mesh-drift-2 32s ease-in-out infinite' }}
      />
      <div
        className="mesh-blob absolute -bottom-[20%] left-[25%] size-[45vmax] rounded-full blur-3xl"
        style={{ background: 'radial-gradient(circle, var(--mesh-3), transparent 65%)', animation: 'mesh-drift-3 38s ease-in-out infinite' }}
      />

      {/* floating particles */}
      {PARTICLES.map((p, i) => (
        <span
          key={i}
          className="particle absolute rounded-full bg-primary"
          style={{
            left: p.left,
            top: p.top,
            width: p.size,
            height: p.size,
            opacity: 0.2,
            animation: `particle-float ${p.duration} ease-in-out ${p.delay} infinite`,
          }}
        />
      ))}
    </div>
  )
}

const SUBSCRIPT_DIGITS = '₀₁₂₃₄₅₆₇₈₉'

function toSubscript(n: number): string {
  return String(n).split('').map(d => SUBSCRIPT_DIGITS[parseInt(d)] || d).join('')
}

export function formatSmallNumber(value: number | null | undefined): string {
  if (value == null || Number.isNaN(value)) return '-'
  if (value === 0) return '0'

  const abs = Math.abs(value)
  const sign = value < 0 ? '-' : ''

  if (abs >= 0.001) {
    if (abs >= 1_000_000_000) {
      return sign + (abs / 1_000_000_000).toFixed(2) + 'B'
    }
    if (abs >= 1_000_000) {
      return sign + (abs / 1_000_000).toFixed(2) + 'M'
    }
    if (abs >= 1_000) {
      return sign + abs.toLocaleString(undefined, { minimumFractionDigits: 2, maximumFractionDigits: 2 })
    }
    if (abs >= 1) {
      return sign + abs.toFixed(4)
    }
    return sign + abs.toFixed(6)
  }

  const full = abs.toFixed(20)
  const match = full.match(/^0\.(0*)([1-9]\d*)/)
  if (!match) return sign + abs.toFixed(6)

  const [, zeros, rest] = match
  const zeroCount = zeros.length

  if (zeroCount <= 3) {
    const decimals = Math.min(6 + zeroCount, 10)
    return sign + abs.toFixed(decimals)
  }

  const truncatedRest = rest.length > 6 ? rest.slice(0, 6) : rest
  return `${sign}0.0${toSubscript(zeroCount)}${truncatedRest}`
}

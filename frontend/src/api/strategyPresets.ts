import martingale from '../assets/presets/martingale.json'
import maCrossover from '../assets/presets/ma-crossover.json'
import macdCrossover from '../assets/presets/macd-crossover.json'
import breakoutStrategy from '../assets/presets/breakout-strategy.json'

export interface StrategyPreset {
  name: string
  description: string
  notes: string[]
  chains: string
}

interface PresetJson {
  name: string
  description: string
  notes: string[]
  chains: Record<string, unknown>[]
}

function loadPreset(json: PresetJson): StrategyPreset {
  return {
    name: json.name,
    description: json.description,
    notes: json.notes,
    chains: JSON.stringify(json.chains)
  }
}

export const strategyPresets: StrategyPreset[] = [
  loadPreset(maCrossover as unknown as PresetJson),
  loadPreset(macdCrossover as unknown as PresetJson),
  loadPreset(breakoutStrategy as unknown as PresetJson),
  loadPreset(martingale as unknown as PresetJson)
]

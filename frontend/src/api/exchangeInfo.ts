export interface ExchangeInfo {
  type: string
  label: string
  color: string
  bgColor: string
  svgUrl: string
}

export const exchangeInfos: ExchangeInfo[] = [
  {
    type: 'Binance',
    label: 'Binance',
    color: '#F3BA2F',
    bgColor: 'rgba(243, 186, 47, 0.12)',
    svgUrl: '/exchanges/binance.svg'
  },
  {
    type: 'OKX',
    label: 'OKX',
    color: '#000000',
    bgColor: 'rgba(0, 0, 0, 0.08)',
    svgUrl: '/exchanges/okx.svg'
  },
  {
    type: 'Gate',
    label: 'Gate.io',
    color: '#1FBA9F',
    bgColor: 'rgba(31, 186, 159, 0.12)',
    svgUrl: '/exchanges/gateio.svg'
  },
  {
    type: 'Bybit',
    label: 'Bybit',
    color: '#F7A600',
    bgColor: 'rgba(247, 166, 0, 0.12)',
    svgUrl: '/exchanges/bybit.svg'
  },
  {
    type: 'HTX',
    label: 'HTX',
    color: '#0C5BFF',
    bgColor: 'rgba(12, 91, 255, 0.12)',
    svgUrl: '/exchanges/htx.svg'
  }
]

export function getExchangeInfo(type: string): ExchangeInfo {
  return exchangeInfos.find(e => e.type === type) ?? exchangeInfos[0]
}

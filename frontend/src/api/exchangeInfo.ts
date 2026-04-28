export interface ExchangeInfo {
  type: string
  label: string
  color: string
  bgColor: string
  icon: string
}

export const exchangeInfos: ExchangeInfo[] = [
  {
    type: 'BINANCE',
    label: 'Binance',
    color: '#F3BA2F',
    bgColor: 'rgba(243, 186, 47, 0.12)',
    icon: 'data:image/svg+xml;base64,PHN2ZyB2aWV3Qm94PSIwIDAgMjQgMjQiIHhtbG5zPSJodHRwOi8vd3d3LnczLm9yZy8yMDAwL3N2ZyI+CiAgPHBhdGggZD0iTTE2LjYyNCAxMy45MjAybDIuNzE3NSAyLjcxNTQtNy4zNTMgNy4zNTMtNy4zNTMtNy4zNTIgMi43MTc1LTIuNzE2NCA0LjYzNTUgNC42NTk1IDQuNjM1Ni00LjY1OTV6bTQuNjM2Ni00LjYzNjZMMjQgMTJsLTIuNzE1NCAyLjcxNjRMMTguNTY4MiAxMmwyLjY5MjQtMi43MTY0em0tOS4yNzIuMDAxbDIuNzE2MyAyLjY5MTQtMi43MTY0IDIuNzE3NHYtLjAwMUw5LjI3MjEgMTJsMi43MTY0LTIuNzE1NHptLTkuMjcyMi0uMDAxTDUuNDA4OCAxMmwtMi42OTE0IDIuNjkyNEwwIDEybDIuNzE2NC0yLjcxNjR6TTExLjk4ODUuMDExNWw3LjM1MyA3LjMyOS0yLjcxNzQgMi43MTU0LTQuNjM1Ni00LjYzNTYtNC42MzU1IDQuNjU5NS0yLjcxNzQtMi43MTU0IDcuMzUzLTcuMzUzeiIgZmlsbD0iI0YzQkEyRiIvPgo8L3N2Zz4K'
  },
  {
    type: 'OKX',
    label: 'OKX',
    color: '#000000',
    bgColor: 'rgba(0, 0, 0, 0.08)',
    icon: 'data:image/svg+xml;base64,PHN2ZyB2aWV3Qm94PSIwIDAgMjQgMjQiIGZpbGw9Im5vbmUiIHhtbG5zPSJodHRwOi8vd3d3LnczLm9yZy8yMDAwL3N2ZyI+CiAgPHJlY3QgeD0iNCIgeT0iNCIgd2lkdGg9IjYiIGhlaWdodD0iNiIgcng9IjEiIGZpbGw9ImN1cnJlbnRDb2xvciIvPgogIDxyZWN0IHg9IjE0IiB5PSI0IiB3aWR0aD0iNiIgaGVpZ2h0PSI2IiByeD0iMSIgZmlsbD0iY3VycmVudENvbG9yIi8+CiAgPHJlY3QgeD0iNCIgeT0iMTQiIHdpZHRoPSI2IiBoZWlnaHQ9IjYiIHJ4PSIxIiBmaWxsPSJjdXJyZW50Q29sb3IiLz4KICA8cmVjdCB4PSIxNCIgeT0iMTQiIHdpZHRoPSI2IiBoZWlnaHQ9IjYiIHJ4PSIxIiBmaWxsPSJjdXJyZW50Q29sb3IiLz4KPC9zdmc+Cg=='
  },
  {
    type: 'GATE',
    label: 'Gate.io',
    color: '#1FBA9F',
    bgColor: 'rgba(31, 186, 159, 0.12)',
    icon: 'data:image/svg+xml;base64,PHN2ZyB2aWV3Qm94PSIwIDAgMjQgMjQiIHhtbG5zPSJodHRwOi8vd3d3LnczLm9yZy8yMDAwL3N2ZyI+CiAgPHJlY3QgeD0iMSIgeT0iMSIgd2lkdGg9IjIyIiBoZWlnaHQ9IjIyIiByeD0iNSIgZmlsbD0iIzFGQkE5RiIvPgogIDxwYXRoIGQ9Ik04IDEyYzAtMi44IDEuOC01IDQuNS01UzE3IDkuMiAxNyAxMnMtMS44IDUtNC41IDUiIHN0cm9rZT0iI2ZmZiIgc3Ryb2tlLXdpZHRoPSIyLjUiIHN0cm9rZS1saW5lY2FwPSJyb3VuZCIgZmlsbD0ibm9uZSIvPgogIDxwYXRoIGQ9Ik0xMi41IDEyaDQiIHN0cm9rZT0iI2ZmZiIgc3Ryb2tlLXdpZHRoPSIyLjUiIHN0cm9rZS1saW5lY2FwPSJyb3VuZCIgZmlsbD0ibm9uZSIvPgo8L3N2Zz4K'
  },
  {
    type: 'BYBIT',
    label: 'Bybit',
    color: '#F7A600',
    bgColor: 'rgba(247, 166, 0, 0.12)',
    icon: 'data:image/svg+xml;base64,PHN2ZyB2aWV3Qm94PSIwIDAgMjQgMjQiIHhtbG5zPSJodHRwOi8vd3d3LnczLm9yZy8yMDAwL3N2ZyI+CiAgPHJlY3QgeD0iMiIgeT0iMiIgd2lkdGg9IjIwIiBoZWlnaHQ9IjIwIiByeD0iNSIgZmlsbD0iI0Y3QTYwMCIvPgogIDxwYXRoIGQ9Ik04LjUgNnYxMiIgc3Ryb2tlPSIjZmZmIiBzdHJva2Utd2lkdGg9IjIuNSIgc3Ryb2tlLWxpbmVjYXA9InJvdW5kIiBmaWxsPSJub25lIi8+CiAgPHBhdGggZD0iTTguNSA2aDMuNWMyLjUgMCA0IDIgNCA0LjVTMTQuNSAxNSAxMiAxNUg4LjUiIHN0cm9rZT0iI2ZmZiIgc3Ryb2tlLXdpZHRoPSIyLjUiIHN0cm9rZS1saW5lY2FwPSJyb3VuZCIgc3Ryb2tlLWxpbmVqb2luPSJyb3VuZCIgZmlsbD0ibm9uZSIvPgo8L3N2Zz4K'
  },
  {
    type: 'HTX',
    label: 'HTX',
    color: '#0C5BFF',
    bgColor: 'rgba(12, 91, 255, 0.12)',
    icon: 'data:image/svg+xml;base64,PHN2ZyB2aWV3Qm94PSIwIDAgMjQgMjQiIHhtbG5zPSJodHRwOi8vd3d3LnczLm9yZy8yMDAwL3N2ZyI+CiAgPHBhdGggZD0iTTEyIDJDOC41IDYuNSA2IDEwIDYgMTRjMCAzLjUgMi41IDYgNiA2czYtMi41IDYtNmMwLTQtMi41LTcuNS02LTEyem0wIDE2Yy0yLjIgMC00LTEuOC00LTQgMC0yLjUgMS41LTUgNC04IDIuNSAzIDQgNS41IDQgOCAwIDIuMi0xLjggNC00IDR6IiBmaWxsPSIjMEM1QkZGIi8+CiAgPHBhdGggZD0iTTEyIDEyYzEuNSAxLjUgMiAzIDIgNC41IDAgMS41LTEgMi41LTIgMi41cy0yLTEtMi0yLjVjMC0xLjUuNS0zIDItNC41eiIgZmlsbD0iIzBDNUJGRiIvPgo8L3N2Zz4K'
  }
]

export function getExchangeInfo(type: string): ExchangeInfo {
  return exchangeInfos.find(e => e.type === type.toUpperCase()) ?? exchangeInfos[0]
}

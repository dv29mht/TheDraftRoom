export type PlayerStat = {
  label: string
  value: number
}

export type PlayerPlaystyle = {
  name: string
  plus?: boolean
}

export type PlayerRole = {
  position: string
  name: string
  familiarity: 0 | 1 | 2
}

export type FcPlayer = {
  id: number
  name: string
  overall: number
  position: string
  alternatePositions: string[]
  club: string
  league: string
  nation: string
  preferredFoot: 'Left' | 'Right'
  weakFoot: number
  skillMoves: number
  height: string
  stats: PlayerStat[]
  playstyles: PlayerPlaystyle[]
  roles: PlayerRole[]
  imageUrl: string
  sourceUrl: string
}

export type PlayerSearchResult = {
  items: FcPlayer[]
  page: number
  pageSize: number
  total: number
  totalPages: number
  datasetLabel: string
}

export type PlayerFilterOptions = {
  positions: string[]
  leagues: string[]
  nations: string[]
}

export type PlayerSearchParams = {
  search?: string
  position?: string
  minOverall?: number
  league?: string
  nation?: string
  sort?: string
  page?: number
  pageSize?: number
}

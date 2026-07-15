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

export type FcPlayerDataset = {
  version: string
  source: string
  players: FcPlayer[]
}

let playerDataset: Promise<FcPlayerDataset> | undefined

export function loadFc26Players() {
  playerDataset ??= fetch('/data/fc26-players.json').then(async (response) => {
    if (!response.ok) throw new Error('The FC 26 player dataset could not be loaded.')
    return response.json() as Promise<FcPlayerDataset>
  })
  return playerDataset
}

export function positionsFor(players: FcPlayer[]) {
  return ['All', ...Array.from(new Set(players.flatMap((player) => [player.position, ...player.alternatePositions])))]
}

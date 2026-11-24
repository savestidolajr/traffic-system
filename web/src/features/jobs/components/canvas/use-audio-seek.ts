import { createContext } from 'react'

export interface AudioSeekValue {
    seekTo: (ms: number) => void
}

const AudioSeekContext = createContext<AudioSeekValue>({
    seekTo: () => {
        /* noop until player is mounted */
    },
})

export { AudioSeekContext }

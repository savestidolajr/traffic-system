import { useRef, useState } from 'react'
import { ICON } from '@/lib/design/icons'
import { Button } from '@/components/ui/button'
import { playSolo } from '@/lib/audio-solo'
import { cn } from '@/lib/utils'

interface SamplePlayButtonProps {
    readonly url: string
    readonly label?: string
    readonly size?: 'icon' | 'sm'
    readonly variant?: 'secondary' | 'ghost' | 'outline'
    readonly className?: string
}

/** Self-contained play/pause for a single audio sample (its own `<audio>`). */
export function SamplePlayButton({
    url,
    label,
    size = 'icon',
    variant = 'secondary',
    className,
}: SamplePlayButtonProps) {
    const audioRef = useRef<HTMLAudioElement>(null)
    const [isPlaying, setIsPlaying] = useState(false)

    // `isPlaying` mirrors the ELEMENT, never the intent: a refused play must not
    // leave a Pause button over silence, and when another player takes the floor
    // this button has to revert without anyone telling it.
    const toggle = (e: React.MouseEvent) => {
        e.stopPropagation()
        const audio = audioRef.current
        if (!audio) return
        if (audio.paused) {
            void playSolo(audio)
        } else {
            audio.pause()
        }
    }

    return (
        <>
            <Button
                type="button"
                size={size}
                variant={variant}
                aria-label={
                    isPlaying ? 'Pause sample' : (label ?? 'Play sample')
                }
                onClick={toggle}
                className={cn('gap-1.5', className)}
            >
                {isPlaying ? (
                    <ICON.pause className="size-4" />
                ) : (
                    <ICON.play className="size-4" />
                )}
                {size !== 'icon' && label}
            </Button>
            <audio
                ref={audioRef}
                src={url}
                onPlay={() => setIsPlaying(true)}
                onPause={() => setIsPlaying(false)}
                onEnded={() => setIsPlaying(false)}
                preload="none"
            />
        </>
    )
}

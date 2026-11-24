import {
    useRef,
    useState,
    useEffect,
    useCallback,
    forwardRef,
    useImperativeHandle,
} from 'react'
import { Play, Pause, RotateCcw, RotateCw } from 'lucide-react'
import { ICON } from '@/lib/design/icons'
import { Slider } from '@/components/ui/slider'
import { Button } from '@/components/ui/button'
import {
    Tooltip,
    TooltipContent,
    TooltipTrigger,
} from '@/components/ui/tooltip'
import { cn } from '@/lib/utils'
import { formatDuration } from '@/lib/formatting'
import { playSolo } from '@/lib/audio-solo'
import { datedFilename, downloadUrl } from '@/lib/transfer'
import { resolveAudioSrc } from '@/lib/api/client'
import { pinMediaSrc } from '../lib/media-url'

export interface AudioWaveformPlayerHandle {
    /** Seek to a millisecond offset and begin playback. */
    seekTo(ms: number): void
}

interface AudioWaveformPlayerProps {
    readonly url: string
    readonly duration?: number
    readonly className?: string
    /** Media preload policy. Review playback opts into auto; other views stay lazy. */
    readonly preload?: 'none' | 'metadata' | 'auto'
    /** Fired on every audio `timeupdate` event with current time in ms. */
    readonly onTimeUpdate?: (ms: number) => void
}

const SKIP_SECONDS = 5

/**
 * Compact audio transport: play/pause, ±5s skips (the replay-last-moment
 * action operators live on), seek slider, elapsed/total, speed. Job audio is
 * a short review asset, so it prebuffers while the review loads rather than
 * making the first Play wait for the proxy-backed stream. Keyboard: space/k
 * toggles, arrow keys skip. No decorative waveform — the script's karaoke
 * highlight is the real position indicator.
 */
export const AudioWaveformPlayer = forwardRef<
    AudioWaveformPlayerHandle,
    AudioWaveformPlayerProps
>(function AudioWaveformPlayer(
    { url, duration, className, preload = 'none', onTimeUpdate },
    ref
) {
    const audioRef = useRef<HTMLAudioElement>(null)
    // Relative proxy paths (jobs/{id}/audio) must be joined onto the API base so
    // the request rides the SWA linked-backend proxy with the auth cookie;
    // absolute SAS/external URLs pass through untouched.
    const resolvedUrl = resolveAudioSrc(url)
    // Hold the loaded source steady across SAS re-signs. The job-detail poll
    // hands us a freshly-signed URL for the same blob every few seconds; without
    // this the <audio> element would reload (and stop) on every poll. Adopt a new
    // URL only when the blob path itself changes (a regenerated audio file).
    const srcRef = useRef(resolvedUrl)
    srcRef.current = pinMediaSrc(srcRef.current, resolvedUrl)
    const src = srcRef.current
    const [isPlaying, setIsPlaying] = useState(false)
    const [currentTime, setCurrentTime] = useState(0)
    const [totalDuration, setTotalDuration] = useState(duration ?? 0)
    const [speed, setSpeed] = useState<1 | 1.5 | 2>(1)

    useImperativeHandle(
        ref,
        () => ({
            seekTo(ms: number) {
                const audio = audioRef.current
                if (!audio) return
                // Before metadata loads the browser clamps currentTime to 0;
                // defer the seek until the media can honour it.
                if (audio.readyState >= HTMLMediaElement.HAVE_METADATA) {
                    audio.currentTime = ms / 1000
                } else {
                    audio.addEventListener(
                        'loadedmetadata',
                        () => {
                            audio.currentTime = ms / 1000
                        },
                        { once: true }
                    )
                }
                void playSolo(audio)
            },
        }),
        []
    )

    const cycleSpeed = useCallback(() => {
        const speeds: readonly (1 | 1.5 | 2)[] = [1, 1.5, 2]
        const next = speeds[(speeds.indexOf(speed) + 1) % speeds.length]
        setSpeed(next)
        if (audioRef.current) audioRef.current.playbackRate = next
    }, [speed])

    // Keep the latest props reachable from the src-change effect below without
    // making them reactive deps (which would reset playback spuriously).
    const durationRef = useRef(duration)
    durationRef.current = duration
    const onTimeUpdateRef = useRef(onTimeUpdate)
    onTimeUpdateRef.current = onTimeUpdate

    // A new blob path means a different bulletin's audio. `srcRef` above pins
    // through SAS re-signs, so `src` only changes on a real change of file — at
    // which point the <audio> element reloads and stops, but `isPlaying` and
    // `currentTime` are ours to reset. Without this, switching jobs mid-playback
    // left the button showing "Pause" and the karaoke rAF loop running against a
    // rewound element, pinning the word highlight to word zero while nothing
    // played. `ReviewPane` does not remount the player per job, so nothing else
    // clears this.
    useEffect(() => {
        setIsPlaying(false)
        setCurrentTime(0)
        setTotalDuration(durationRef.current ?? 0)
        onTimeUpdateRef.current?.(0)
    }, [src])

    useEffect(() => {
        const audio = audioRef.current
        if (!audio) return

        const onTimeUpdateEvent = () => {
            const ms = audio.currentTime * 1000
            setCurrentTime(audio.currentTime)
            onTimeUpdate?.(ms)
        }

        const onLoadedMetadata = () => setTotalDuration(audio.duration)
        // Track the ELEMENT, not the intent. A refused play() used to flip the
        // button to Pause over silence (abort the wav and it sat there with
        // readyState 0), and a player displaced by another one never reverted.
        const onPlay = () => setIsPlaying(true)
        const onPause = () => setIsPlaying(false)
        const onEnded = () => setIsPlaying(false)

        audio.addEventListener('timeupdate', onTimeUpdateEvent)
        audio.addEventListener('loadedmetadata', onLoadedMetadata)
        audio.addEventListener('ended', onEnded)
        audio.addEventListener('play', onPlay)
        audio.addEventListener('pause', onPause)

        return () => {
            audio.removeEventListener('timeupdate', onTimeUpdateEvent)
            audio.removeEventListener('loadedmetadata', onLoadedMetadata)
            audio.removeEventListener('ended', onEnded)
            audio.removeEventListener('play', onPlay)
            audio.removeEventListener('pause', onPause)
        }
    }, [src, onTimeUpdate])

    // Karaoke precision. `timeupdate` fires only ~4×/s, so the word highlight
    // can trail the audio by up to ~250ms (a fraction of a word) even though the
    // ElevenLabs word timings themselves are accurate. While playing, sample
    // audio.currentTime every animation frame and push it to the highlight so the
    // active word tracks the real playhead within ~16ms. The slider stays on
    // `timeupdate` (4Hz is ample for a progress bar), and `timeupdate` still
    // drives the highlight while paused/seeking when this loop is idle.
    useEffect(() => {
        if (!isPlaying) return
        const audio = audioRef.current
        if (!audio) return

        let raf = 0
        const tick = () => {
            onTimeUpdate?.(audio.currentTime * 1000)
            raf = requestAnimationFrame(tick)
        }
        raf = requestAnimationFrame(tick)
        return () => cancelAnimationFrame(raf)
    }, [isPlaying, onTimeUpdate])

    const togglePlay = useCallback(() => {
        const audio = audioRef.current
        if (!audio) return

        if (isPlaying) {
            audio.pause()
        } else {
            void playSolo(audio)
        }
    }, [isPlaying])

    const skipBy = useCallback(
        (deltaSec: number) => {
            const audio = audioRef.current
            if (!audio) return
            const next = Math.min(
                Math.max(audio.currentTime + deltaSec, 0),
                totalDuration || audio.duration || 0
            )
            audio.currentTime = next
            setCurrentTime(next)
        },
        [totalDuration]
    )

    const handleSeek = useCallback((value: number[]) => {
        const audio = audioRef.current
        if (!audio) return

        audio.currentTime = value[0]
        setCurrentTime(value[0])
    }, [])

    const handleKeyDown = useCallback(
        (event: React.KeyboardEvent) => {
            // Let native controls on buttons and sliders handle themselves
            if ((event.target as Element).closest('button, [role="slider"]'))
                return

            // Prevent handled keys, especially "k", reaching the global queue shortcut.
            switch (event.key) {
                case ' ':
                case 'k':
                    event.preventDefault()
                    event.stopPropagation()
                    togglePlay()
                    break
                case 'ArrowRight':
                    event.preventDefault()
                    event.stopPropagation()
                    skipBy(SKIP_SECONDS)
                    break
                case 'ArrowLeft':
                    event.preventDefault()
                    event.stopPropagation()
                    skipBy(-SKIP_SECONDS)
                    break
            }
        },
        [togglePlay, skipBy]
    )

    return (
        <div
            className={cn(
                'flex items-center gap-2 rounded-lg border bg-background/40 px-3 py-2',
                className
            )}
            tabIndex={0}
            role="group"
            aria-label="Audio player"
            onKeyDown={handleKeyDown}
        >
            <audio ref={audioRef} src={src} preload={preload} />

            <Tooltip>
                <TooltipTrigger asChild>
                    <Button
                        type="button"
                        size="icon"
                        variant="default"
                        className="size-9 shrink-0 rounded-full"
                        onClick={togglePlay}
                        aria-label={isPlaying ? 'Pause' : 'Play'}
                    >
                        {isPlaying ? <Pause /> : <Play className="ml-0.5" />}
                    </Button>
                </TooltipTrigger>
                <TooltipContent side="top">
                    {isPlaying ? 'Pause' : 'Play'} (space)
                </TooltipContent>
            </Tooltip>

            <Tooltip>
                <TooltipTrigger asChild>
                    <Button
                        type="button"
                        size="icon"
                        variant="ghost"
                        className="size-8 shrink-0"
                        onClick={() => skipBy(-SKIP_SECONDS)}
                        aria-label="Back 5 seconds"
                    >
                        <RotateCcw />
                    </Button>
                </TooltipTrigger>
                <TooltipContent side="top">Back 5s (←)</TooltipContent>
            </Tooltip>
            <Tooltip>
                <TooltipTrigger asChild>
                    <Button
                        type="button"
                        size="icon"
                        variant="ghost"
                        className="size-8 shrink-0"
                        onClick={() => skipBy(SKIP_SECONDS)}
                        aria-label="Forward 5 seconds"
                    >
                        <RotateCw />
                    </Button>
                </TooltipTrigger>
                <TooltipContent side="top">Forward 5s (→)</TooltipContent>
            </Tooltip>

            <Slider
                value={[currentTime]}
                max={totalDuration || 1}
                step={0.1}
                onValueChange={handleSeek}
                className="mx-1 flex-1"
                aria-label="Seek"
            />

            <span className="shrink-0 font-mono text-xs tabular-nums text-muted-foreground">
                {formatDuration(currentTime)}
                <span className="text-muted-foreground-subtle">
                    {' / '}
                    {formatDuration(totalDuration)}
                </span>
            </span>

            <Button
                type="button"
                variant="ghost"
                size="icon"
                onClick={cycleSpeed}
                // Secondary transport controls are all 32px (`size-8`); only the
                // primary play button is 36px. `size="sm"` made this one 36 too.
                className="h-8 w-11 shrink-0 font-mono text-xs"
                aria-label={`Playback speed ${speed}x`}
            >
                {speed}x
            </Button>

            <Tooltip>
                <TooltipTrigger asChild>
                    <Button
                        type="button"
                        size="icon"
                        variant="ghost"
                        className="size-8 shrink-0"
                        onClick={() =>
                            downloadUrl(
                                src,
                                datedFilename('bulletin-audio', 'wav')
                            )
                        }
                        aria-label="Download audio"
                    >
                        <ICON.download />
                    </Button>
                </TooltipTrigger>
                <TooltipContent side="top">Download audio</TooltipContent>
            </Tooltip>
        </div>
    )
})

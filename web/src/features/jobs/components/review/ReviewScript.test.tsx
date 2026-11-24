// @vitest-environment jsdom
import '@testing-library/jest-dom/vitest'
import { describe, it, expect, vi, afterEach } from 'vitest'
import type { ReactNode } from 'react'
import { TooltipProvider } from '@/components/ui/tooltip'
import { render, screen, cleanup, fireEvent } from '@testing-library/react'
import userEvent, { type UserEvent } from '@testing-library/user-event'
import { ReviewScript } from './ReviewScript'
import * as jobHooks from '../../api/job-hooks'
import type { PronunciationMatchResult } from '../../lib/pronunciation-matches'
import type { PronunciationDraftContext } from './ReviewScript'
import { isScriptTooLongToFit } from './review-script-utils'
import type { BulletinEditingGuidance, ScriptRoadPhrase } from '../../types'
import type { ScriptMarkActions } from '../ScriptWordMark'

// The tone picker is a Radix Select, which measures and pointer-captures.
// jsdom has neither, so stub what it reaches for.
class ResizeObserverStub {
    observe() {}
    unobserve() {}
    disconnect() {}
}
vi.stubGlobal('ResizeObserver', ResizeObserverStub)
Element.prototype.scrollIntoView = vi.fn()
Element.prototype.hasPointerCapture = vi.fn(() => false)
Element.prototype.setPointerCapture = vi.fn()
Element.prototype.releasePointerCapture = vi.fn()

afterEach(() => {
    cleanup()
})

// ---------------------------------------------------------------------------
// isScriptTooLongToFit -- pure threshold check backing the hint/button gate.
// ---------------------------------------------------------------------------
describe('isScriptTooLongToFit', () => {
    it('is false within and just past the aim band (the audio pipeline can still speed it up)', () => {
        expect(isScriptTooLongToFit(79, 89)).toBe(false)
        expect(isScriptTooLongToFit(95, 89)).toBe(false)
    })

    it('is true once the read is far enough over that no speed-up can compress it into the slot', () => {
        expect(isScriptTooLongToFit(400, 89)).toBe(true)
    })

    it('sits at the documented ~1.6x-of-ceiling boundary', () => {
        expect(isScriptTooLongToFit(142, 89)).toBe(false) // 89 * 1.6 == 142.4
        expect(isScriptTooLongToFit(143, 89)).toBe(true)
    })
})

// ---------------------------------------------------------------------------
// ReviewScript -- end-to-end hint text + Re-voice enablement for a 30s slot.
// ---------------------------------------------------------------------------
const guidance: BulletinEditingGuidance = {
    targetWordCount: 83,
    minWordCount: 77,
    maxWordCount: 89,
    wordsPerMinute: 170,
    targetDurationSeconds: 30,
}

function words(count: number): string {
    return Array.from({ length: count }, (_, i) => `word${i}`).join(' ')
}
function renderScript(ui: ReactNode) {
    return render(<TooltipProvider>{ui}</TooltipProvider>)
}

async function enterEditModeAndType(draft: string) {
    const user = userEvent.setup()
    await user.click(screen.getByRole('button', { name: /edit/i }))
    const editor = screen.getByRole('textbox', { name: 'Edit script' })
    fireEvent.change(editor, { target: { value: draft } })
    return screen.getByRole('button', { name: /re-voice/i })
}

describe('ReviewScript over-length guidance', () => {
    it('shows the fits-the-slot hint and an enabled Re-voice for a normal-length script', async () => {
        renderScript(
            <ReviewScript
                toneCapability={{ model: 'eleven_v3' }}
                text="original short script"
                canEdit
                onRevoice={vi.fn()}
                isRevoicing={false}
                editingGuidance={guidance}
            />
        )

        const revoiceButton = await enterEditModeAndType(words(79))

        expect(screen.getByText(/fits the 30s slot/)).toBeInTheDocument()
        expect(revoiceButton).not.toBeDisabled()
    })

    it('shows a cannot-fit warning and disables Re-voice for a physically over-length script', async () => {
        renderScript(
            <ReviewScript
                toneCapability={{ model: 'eleven_v3' }}
                text="original short script"
                canEdit
                onRevoice={vi.fn()}
                isRevoicing={false}
                editingGuidance={guidance}
            />
        )

        const revoiceButton = await enterEditModeAndType(words(400))

        expect(
            screen.getByText(/too long for 30s.*cannot fit the slot/)
        ).toBeInTheDocument()
        expect(revoiceButton).toBeDisabled()
    })
})
describe('ReviewScript source labels', () => {
    const sourceText =
        'Intro. The M7 is busy. Warringah Road is clear. Weather is sunny. Fuel prices are steady.'
    const sourceSentences = [
        { index: 0, text: 'Intro.', startWord: 0, endWord: 1 },
        {
            index: 1,
            text: 'The M7 is busy.',
            startWord: 1,
            endWord: 5,
        },
        {
            index: 2,
            text: 'Warringah Road is clear.',
            startWord: 5,
            endWord: 9,
        },
        {
            index: 3,
            text: 'Weather is sunny.',
            startWord: 9,
            endWord: 12,
        },
        {
            index: 4,
            text: 'Fuel prices are steady.',
            startWord: 12,
            endWord: 16,
        },
    ] as const

    it('shows each mixed-source line with its actual source labels', () => {
        renderScript(
            <ReviewScript
                toneCapability={{ model: 'eleven_v3' }}
                text={sourceText}
                canEdit={false}
                onRevoice={vi.fn()}
                isRevoicing={false}
                sentences={sourceSentences}
                bySentence={[
                    { incidentKeys: [], sourceKinds: [] },
                    { incidentKeys: ['m7'], sourceKinds: ['traffic'] },
                    {
                        incidentKeys: [],
                        sourceKinds: ['route', 'good-run'],
                    },
                    { incidentKeys: [], sourceKinds: ['weather'] },
                    { incidentKeys: [], sourceKinds: ['fuel'] },
                ]}
            />
        )

        expect(
            screen
                .getByRole('img', { name: 'Source: incident traffic' })
                .closest('[data-sentence-index="1"]')
        ).toBeInTheDocument()
        expect(
            screen
                .getByRole('img', { name: 'Source: monitored route' })
                .closest('[data-sentence-index="2"]')
        ).toBeInTheDocument()
        expect(
            screen
                .getByRole('img', { name: 'Source: good run' })
                .closest('[data-sentence-index="2"]')
        ).toBeInTheDocument()
        expect(
            screen
                .getByRole('img', { name: 'Source: weather' })
                .closest('[data-sentence-index="3"]')
        ).toBeInTheDocument()
        expect(
            screen
                .getByRole('img', { name: 'Source: fuel' })
                .closest('[data-sentence-index="4"]')
        ).toBeInTheDocument()
    })

    it('leaves intro and outro lines unlabeled when lineage is empty or absent', () => {
        const text = 'Intro. Traffic is moving well. Outro.'
        renderScript(
            <ReviewScript
                toneCapability={{ model: 'eleven_v3' }}
                text={text}
                canEdit={false}
                onRevoice={vi.fn()}
                isRevoicing={false}
                sentences={[
                    { index: 0, text: 'Intro.', startWord: 0, endWord: 1 },
                    {
                        index: 1,
                        text: 'Traffic is moving well.',
                        startWord: 1,
                        endWord: 5,
                    },
                    { index: 2, text: 'Outro.', startWord: 5, endWord: 6 },
                ]}
                bySentence={[
                    { incidentKeys: [], sourceKinds: [] },
                    { incidentKeys: [], sourceKinds: ['traffic'] },
                    { incidentKeys: [] },
                ]}
            />
        )
        const introLine = screen
            .getByText('Intro.')
            .closest('[data-sentence-index="0"]')
        const trafficLine = screen
            .getByText('moving')
            .closest('[data-sentence-index="1"]')
        const outroLine = screen
            .getByText('Outro.')
            .closest('[data-sentence-index="2"]')

        expect(introLine?.querySelector('[data-source-kind]')).toBeNull()
        expect(
            trafficLine?.querySelector('[data-source-kind="traffic"]')
        ).toBeInTheDocument()
        expect(outroLine?.querySelector('[data-source-kind]')).toBeNull()
    })
})

// ---------------------------------------------------------------------------
// ReviewScript -- exact-version pronunciation treatment (read + live edit).
// ---------------------------------------------------------------------------
const pronunciationScript = 'Take it slow through Greensborough this morning.'

const melbourneRoadsDictionary = {
    id: 'lib-1',
    dictionaryId: 'el-dict-1',
    name: 'Melbourne Roads',
    description: null,
    status: 'active' as const,
    currentVersion: 4,
    createdAt: '2024-01-01T00:00:00Z',
    updatedAt: '2024-01-01T00:00:00Z',
    createdBy: null,
    assignedStations: [],
    rules: [],
    versions: [
        {
            version: 4,
            elevenLabsVersionId: 'ver-4',
            rules: [
                {
                    type: 'alias' as const,
                    stringToReplace: 'Greensborough',
                    replacement: 'Greenybro',
                    alphabet: null,
                    caseSensitive: false,
                    wordBoundaries: true,
                    appliesOnAllModels: true,
                },
            ],
            changeNote: null,
            createdAt: '2024-01-01T00:00:00Z',
            createdBy: null,
        },
    ],
}

const pronunciationDraftContext: PronunciationDraftContext = {
    providerDictionaryId: 'el-dict-1',
    providerVersionId: 'ver-4',
    voiceModel: 'eleven_multilingual_v2',
    dictionaries: [melbourneRoadsDictionary],
}

function exactPronunciationMatch(): PronunciationMatchResult {
    const start = pronunciationScript.indexOf('Greensborough')
    return {
        dictionaryName: 'Melbourne Roads',
        libraryId: 'lib-1',
        versionNumber: 4,
        providerVersionId: 'ver-4',
        resolution: 'exact',
        matches: [
            {
                canonicalText: 'Greensborough',
                rule: 'Greensborough',
                replacement: 'Greenybro',
                start,
                end: start + 'Greensborough'.length,
            },
        ],
    }
}

const pronunciationWordTimings = [
    { word: 'Take', startMs: 0 },
    { word: 'it', startMs: 100 },
    { word: 'slow', startMs: 200 },
    { word: 'through', startMs: 300 },
    { word: 'Greensborough', startMs: 400 },
    { word: 'this', startMs: 500 },
    { word: 'morning.', startMs: 600 },
]

const pronunciationSentences = [
    {
        index: 0,
        text: pronunciationScript,
        startWord: 0,
        endWord: pronunciationWordTimings.length,
    },
]

it('seeks to a selected timed word without restoring seek hover copy', async () => {
    const onWordSeek = vi.fn()
    renderScript(
        <ReviewScript
            toneCapability={{ model: 'eleven_v3' }}
            text={pronunciationScript}
            canEdit
            onRevoice={vi.fn()}
            isRevoicing={false}
            wordTimings={pronunciationWordTimings}
            sentences={pronunciationSentences}
            onWordSeek={onWordSeek}
        />
    )

    const word = screen.getByText('slow')
    expect(word).not.toHaveAttribute('title')
    await userEvent.click(word)
    expect(onWordSeek).toHaveBeenCalledWith(200)
})

describe('ReviewScript pronunciation treatment', () => {
    it('seeks from a pronunciation word while opening its popover without seek copy', async () => {
        const onWordSeek = vi.fn()
        renderScript(
            <ReviewScript
                toneCapability={{ model: 'eleven_v3' }}
                text={pronunciationScript}
                canEdit
                onRevoice={vi.fn()}
                isRevoicing={false}
                wordTimings={pronunciationWordTimings}
                sentences={pronunciationSentences}
                onWordSeek={onWordSeek}
                pronunciationMatch={exactPronunciationMatch()}
            />
        )

        expect(
            screen.getByLabelText(
                'Pronunciation dictionary · Melbourne Roads · version 4'
            )
        ).toBeInTheDocument()

        // Canonical text stays untouched; selecting the word seeks without
        // exposing timing copy, while hover retains the rich explanation.
        const marker = screen.getByRole('button', {
            name: /Greensborough is read on air as Greenybro/,
        })
        expect(marker).toHaveTextContent('Greensborough')
        expect(marker.tagName.toLowerCase()).toBe('mark')
        expect(marker).not.toHaveAttribute('title')
        expect(marker).not.toHaveAccessibleName(/seek to/i)

        // A dictionary-only mark in the read-only script offers no action, so it
        // explains itself in ONE hover rather than opening a popover whose only
        // content is a "?" hiding the explanation.
        await userEvent.hover(marker)
        const tip = await screen.findByRole('tooltip')
        expect(tip).toHaveTextContent(/reads this word differently on air/)
        expect(tip).toHaveTextContent(
            'Said as "Greenybro" (Melbourne Roads v4).'
        )
        expect(
            document.querySelector('[data-radix-popper-content-wrapper] input')
        ).toBeNull()

        // Clicking still seeks (the script's own word handler owns that).
        await userEvent.click(marker)
        expect(onWordSeek).toHaveBeenCalledWith(400)
    })

    it('keeps a live glowing pronunciation popover inside the editor', async () => {
        renderScript(
            <ReviewScript
                toneCapability={{ model: 'eleven_v3' }}
                text={pronunciationScript}
                canEdit
                onRevoice={vi.fn()}
                isRevoicing={false}
                pronunciationContext={pronunciationDraftContext}
            />
        )

        const user = userEvent.setup()
        await user.click(screen.getByRole('button', { name: /edit/i }))

        const marker = screen.getByRole('button', {
            name: /Greensborough is read on air as Greenybro/,
        })
        expect(marker).toHaveAttribute('data-pronunciation-word')
        await user.hover(marker)
        expect(await screen.findByRole('tooltip')).toHaveTextContent(
            'Greenybro'
        )
        expect(
            screen.queryByText(/pronunciation rule.*will apply/i)
        ).not.toBeInTheDocument()
        expect(
            screen.queryByText('On-air pronunciation')
        ).not.toBeInTheDocument()
    })

    it('drops the inline marker as soon as an edit removes the matching term', () => {
        renderScript(
            <ReviewScript
                toneCapability={{ model: 'eleven_v3' }}
                text={pronunciationScript}
                canEdit
                onRevoice={vi.fn()}
                isRevoicing={false}
                pronunciationContext={pronunciationDraftContext}
            />
        )

        fireEvent.click(screen.getByRole('button', { name: /edit/i }))
        expect(
            screen.getByRole('button', {
                name: /Greensborough is read on air as Greenybro/,
            })
        ).toBeInTheDocument()

        const editor = screen.getByRole('textbox', { name: 'Edit script' })
        fireEvent.change(editor, {
            target: {
                value: 'Take it slow through the north-east this morning.',
            },
        })

        expect(
            screen.queryByRole('button', {
                name: /Greensborough is read on air as Greenybro/,
            })
        ).not.toBeInTheDocument()
        expect(
            screen.queryByText(/pronunciation rule.*will apply/i)
        ).not.toBeInTheDocument()
    })

    it('keeps each sentence on its own editable line and revoices canonical prose', () => {
        const onRevoice = vi.fn()
        const { container } = renderScript(
            <ReviewScript
                toneCapability={{ model: 'eleven_v3' }}
                text="First sentence. Greensborough is slow. Third sentence."
                canEdit
                onRevoice={onRevoice}
                isRevoicing={false}
                pronunciationContext={pronunciationDraftContext}
            />
        )

        fireEvent.click(screen.getByRole('button', { name: /edit/i }))
        const editor = screen.getByRole('textbox', {
            name: 'Edit script',
        }) as HTMLTextAreaElement
        const mirror = container.querySelector('[data-script-mirror]')
        expect(editor.value.split('\n')).toHaveLength(3)
        // The mirror paints exactly the editable text — that parity is what
        // keeps the caret under the glyph the operator is looking at.
        expect(mirror?.textContent).toBe(editor.value)

        fireEvent.change(editor, {
            target: {
                value: 'First sentence. Greensborough is very slow. Third sentence.',
            },
        })
        expect(editor.value.split('\n')).toHaveLength(3)
        expect(mirror?.textContent).toBe(editor.value)
        fireEvent.click(screen.getByRole('button', { name: /re-voice/i }))

        // No tone set, so no tagged read travels with the words.
        expect(onRevoice).toHaveBeenCalledWith(
            'First sentence. Greensborough is very slow. Third sentence.',
            undefined
        )
    })

    it('re-voices on a tone alone, sending the tagged read beside the unchanged words', async () => {
        const user = userEvent.setup()
        const onRevoice = vi.fn()
        renderScript(
            <ReviewScript
                toneCapability={{ model: 'eleven_v3' }}
                text="First sentence. Greensborough is slow."
                canEdit
                onRevoice={onRevoice}
                isRevoicing={false}
                pronunciationContext={pronunciationDraftContext}
            />
        )

        await user.click(screen.getByRole('button', { name: /edit/i }))
        // Words untouched: Re-voice is disabled until the draft changes.
        expect(screen.getByRole('button', { name: /re-voice/i })).toBeDisabled()

        // The tag is literal text in the script: type it where it should sit.
        const textarea = screen.getByLabelText('Edit script')
        fireEvent.change(textarea, {
            target: {
                value: 'First sentence. [professional] Greensborough is slow.',
            },
        })

        const revoice = screen.getByRole('button', { name: /re-voice/i })
        expect(revoice).not.toBeDisabled()
        await user.click(revoice)

        // The words are stripped for the pipeline; the tag rides along in the
        // exact position the operator placed it.
        expect(onRevoice).toHaveBeenCalledWith(
            'First sentence. Greensborough is slow.',
            'First sentence. [professional] Greensborough is slow.'
        )
    })

    it('shows an explicit warning instead of falling back to current rules when the exact version is unavailable', () => {
        const missingVersionMatch: PronunciationMatchResult = {
            dictionaryName: 'Melbourne Roads',
            libraryId: 'lib-1',
            versionNumber: null,
            providerVersionId: 'ver-stale',
            resolution: 'missing-version',
            matches: [],
        }

        renderScript(
            <ReviewScript
                toneCapability={{ model: 'eleven_v3' }}
                text={pronunciationScript}
                canEdit
                onRevoice={vi.fn()}
                isRevoicing={false}
                pronunciationMatch={missingVersionMatch}
            />
        )

        expect(
            screen.getByLabelText(/exact on-air version unavailable/)
        ).toBeInTheDocument()
        expect(screen.queryByText(/on-air read/)).not.toBeInTheDocument()
        expect(screen.queryByTitle(/On air:/)).not.toBeInTheDocument()
    })
})

// ---------------------------------------------------------------------------
// ReviewScript -- teaching a pronunciation the AI never flagged. The operator's
// standing complaint: a word only became fixable once something marked it.
//
// The gesture is a hover DWELL, deliberately not a click: clicking a word seeks
// the audio, which an operator does constantly while checking a read, and a panel
// on every one of those clicks made the script unusable to click through.
// ---------------------------------------------------------------------------
function renderRespellableScript(
    onAddRule: ScriptMarkActions['onAddRule'],
    dictionaryName: string | null = 'Melbourne Roads',
    onWordSeek: (startMs: number) => void = vi.fn(),
    roadPhraseSpans: readonly ScriptRoadPhrase[] | null = null
) {
    const actions: ScriptMarkActions = {
        onVerify: vi.fn(),
        onUnverify: vi.fn(),
        onAddRule,
    }
    return renderScript(
        <ReviewScript
            toneCapability={{ model: 'eleven_v3' }}
            text={pronunciationScript}
            canEdit
            onRevoice={vi.fn()}
            isRevoicing={false}
            wordTimings={pronunciationWordTimings}
            sentences={pronunciationSentences}
            onWordSeek={onWordSeek}
            markActions={actions}
            markDictionaryName={dictionaryName}
            roadPhraseSpans={roadPhraseSpans}
        />
    )
}

/** Comfortably past ScriptRenderer's 400ms dwell, for asserting nothing opened. */
const RESPELL_DWELL_GRACE_MS = 600

/** `findBy*` polls for a second, which outlasts the dwell without fake timers. */
const dwellOn = (word: string) => fireEvent.mouseOver(screen.getByText(word))

describe('ReviewScript hover-to-respell', () => {
    it('saves a rule for an unflagged word after the pointer rests on it', async () => {
        const onAddRule = vi.fn().mockResolvedValue(undefined)
        renderRespellableScript(onAddRule)

        dwellOn('slow')

        const field = await screen.findByLabelText('Respelling for slow')
        await userEvent.type(field, 'slo')
        await userEvent.click(screen.getByRole('button', { name: 'Save' }))

        expect(onAddRule).toHaveBeenCalledWith('slow', 'slo')
    })

    it('seeks WITHOUT opening a panel when a word is clicked', async () => {
        const onWordSeek = vi.fn()
        renderRespellableScript(
            vi.fn().mockResolvedValue(undefined),
            'Melbourne Roads',
            onWordSeek
        )

        await userEvent.click(screen.getByText('slow'))

        expect(onWordSeek).toHaveBeenCalledWith(200)
        expect(
            screen.queryByLabelText('Respelling for slow')
        ).not.toBeInTheDocument()
    })

    it('opens nothing while the pointer only crosses words', async () => {
        renderRespellableScript(vi.fn().mockResolvedValue(undefined))

        dwellOn('slow')
        dwellOn('through')
        dwellOn('this')

        // The dwell restarts on each new word, so only the last one can open —
        // and the two it passed over must not.
        expect(
            await screen.findByLabelText('Respelling for this')
        ).toBeInTheDocument()
        expect(
            screen.queryByLabelText('Respelling for slow')
        ).not.toBeInTheDocument()
        expect(
            screen.queryByLabelText('Respelling for through')
        ).not.toBeInTheDocument()
    })

    it('never takes focus, so the audio player keeps its keyboard control', async () => {
        renderRespellableScript(vi.fn().mockResolvedValue(undefined))
        const before = document.activeElement

        dwellOn('slow')
        await screen.findByLabelText('Respelling for slow')

        expect(document.activeElement).toBe(before)
    })

    it('drops the punctuation the script carries but a rule must not', async () => {
        renderRespellableScript(vi.fn().mockResolvedValue(undefined))

        dwellOn('morning.')

        expect(
            await screen.findByLabelText('Respelling for morning')
        ).toBeInTheDocument()
    })

    it('offers nothing when no dictionary is attached to the bulletin', async () => {
        renderRespellableScript(vi.fn().mockResolvedValue(undefined), null)

        dwellOn('slow')

        const settled = Promise.withResolvers<void>()
        setTimeout(settled.resolve, RESPELL_DWELL_GRACE_MS)
        await settled.promise

        expect(
            screen.queryByLabelText('Respelling for slow')
        ).not.toBeInTheDocument()
    })

    it('lets the operator widen the hovered word into the phrase they heard', async () => {
        const onAddRule = vi.fn().mockResolvedValue(undefined)
        renderRespellableScript(onAddRule)

        dwellOn('Greensborough')

        const target = await screen.findByLabelText('Word or phrase to respell')
        expect(target).toHaveValue('Greensborough')
        await userEvent.clear(target)
        await userEvent.type(target, 'through Greensborough')
        await userEvent.type(
            screen.getByLabelText('Respelling for through Greensborough'),
            'thru Greensbra'
        )
        await userEvent.click(screen.getByRole('button', { name: 'Save' }))

        expect(onAddRule).toHaveBeenCalledWith(
            'through Greensborough',
            'thru Greensbra'
        )
    })

    it('selects the complete server-resolved road phrase from any word in it', async () => {
        const onAddRule = vi.fn().mockResolvedValue(undefined)
        const text = 'Take it slow through Gillies Highway this morning.'
        const start = text.indexOf('Gillies Highway')
        renderScript(
            <ReviewScript
                toneCapability={{ model: 'eleven_v3' }}
                text={text}
                canEdit
                onRevoice={vi.fn()}
                isRevoicing={false}
                markActions={{
                    onVerify: vi.fn(),
                    onUnverify: vi.fn(),
                    onAddRule,
                }}
                markDictionaryName="Melbourne Roads"
                roadPhraseSpans={[
                    {
                        roadName: 'Gillies Highway',
                        start,
                        end: start + 'Gillies Highway'.length,
                        text: 'Gillies Highway',
                    },
                ]}
            />
        )

        dwellOn('Gillies')

        const roadTarget = await screen.findByLabelText(
            'Word or phrase to respell'
        )
        expect(roadTarget).toHaveValue('Gillies Highway')
        const replacement = screen.getByLabelText(
            'Respelling for Gillies Highway'
        )
        await userEvent.type(replacement, 'Gillies Range Road')
        await userEvent.click(screen.getByRole('button', { name: 'Save' }))

        expect(onAddRule).toHaveBeenCalledWith(
            'Gillies Highway',
            'Gillies Range Road'
        )
    })
})

// ---------------------------------------------------------------------------
// Rewrite for REGISTER. The operator describes the read in a sentence and the
// agent reshapes the WORDS. It must never voice anything on its own: changed
// words have to be read by a human and reviewed by the safety agent on submit —
// and, like the content rewrite beside it, they arrive as a PREVIEW rather than
// overwriting the draft, because "spot what moved by re-reading 80 words" is
// not a review and there was no undo.
// ---------------------------------------------------------------------------
describe('ReviewScript rewrite for register', () => {
    afterEach(() => {
        vi.restoreAllMocks()
    })

    const ORIGINAL = 'The Monash Freeway is closed citybound at Toorak Road.'
    const REWRITTEN =
        '[quietly] Some sad news. The Monash Freeway is CLOSED citybound at Toorak Road.'

    function renderForRetone(onRevoice = vi.fn()) {
        return {
            onRevoice,
            ...renderScript(
                <ReviewScript
                    jobId="job-1"
                    text={ORIGINAL}
                    canEdit
                    onRevoice={onRevoice}
                    isRevoicing={false}
                    toneCapability={{ model: 'eleven_v3' }}
                />
            ),
        }
    }

    function stubRetone() {
        vi.spyOn(jobHooks, 'retoneScript').mockResolvedValue({
            script: REWRITTEN,
            wordsBefore: 8,
            wordsAfter: 10,
            tags: ['[quietly]'],
            preservedLines: 0,
        })
    }

    async function runRetone(user: UserEvent, ask = 'calmer please') {
        await user.click(screen.getByRole('button', { name: /edit/i }))
        await user.type(
            screen.getByLabelText('Describe the read you want'),
            ask
        )
        await user.click(screen.getByRole('button', { name: 'Rewrite' }))
    }

    it('previews the rewrite for the operator to read, voicing nothing', async () => {
        stubRetone()
        const { onRevoice } = renderForRetone()
        const user = userEvent.setup()

        await runRetone(user, "talk about this more calmly, it's about a death")

        // The rewrite is on screen as a diff…
        expect(
            (await screen.findAllByText(/Some sad news/)).length
        ).toBeGreaterThan(0)
        // …the draft is untouched until the operator applies it…
        expect(
            screen.getByRole('textbox', { name: 'Edit script' })
        ).toHaveValue(ORIGINAL)
        // …and nothing has been sent to the voice.
        expect(onRevoice).not.toHaveBeenCalled()
    })

    it('lands the rewrite in the editor only when the operator applies it', async () => {
        stubRetone()
        const { onRevoice } = renderForRetone()
        const user = userEvent.setup()

        await runRetone(user)
        await screen.findByText(/Preview ·/)
        await user.click(
            screen.getByRole('button', { name: 'Apply to editor' })
        )

        expect(
            await screen.findByDisplayValue(new RegExp('quietly'))
        ).toBeInTheDocument()
        expect(onRevoice).not.toHaveBeenCalled()
    })

    it('reports the word change and the tags so an operator can see it still fits the slot', async () => {
        stubRetone()
        renderForRetone()
        const user = userEvent.setup()

        await runRetone(user)

        const summary = await screen.findByText(/Preview ·/)
        expect(summary.textContent?.replace(/\s+/g, ' ')).toContain(
            'Delivery · 8 → 10 words · [quietly]'
        )
    })

    it('surfaces a refusal instead of silently leaving the draft alone', async () => {
        // The writer rejects a rewrite that lost a fact or blew the slot budget. That reason is
        // actionable, so it must reach the operator rather than look like nothing happened.
        vi.spyOn(jobHooks, 'retoneScript').mockRejectedValue(
            new Error('it dropped or altered these facts: Toorak Road.')
        )
        renderForRetone()
        const user = userEvent.setup()

        await runRetone(user)

        expect(await screen.findByRole('alert')).toHaveTextContent(
            /dropped or altered these facts/
        )
        // The draft is left exactly as the operator had it — a refusal must not half-apply.
        expect(
            screen.getByRole('textbox', { name: 'Edit script' })
        ).toHaveValue(ORIGINAL)
    })

    it('will not call out with an empty description', async () => {
        const spy = vi.spyOn(jobHooks, 'retoneScript')
        renderForRetone()
        const user = userEvent.setup()

        await user.click(screen.getByRole('button', { name: /edit/i }))
        expect(screen.getByRole('button', { name: 'Rewrite' })).toBeDisabled()
        expect(spy).not.toHaveBeenCalled()
    })
})

describe('ReviewScript tone controls on a voice that cannot use tags', () => {
    const SCRIPT = 'The Monash Freeway is closed citybound at Toorak Road.'

    function renderWithModel(model: string) {
        return renderScript(
            <ReviewScript
                jobId="job-1"
                text={SCRIPT}
                canEdit
                onRevoice={vi.fn()}
                isRevoicing={false}
                toneCapability={{ model }}
            />
        )
    }

    it('offers no tone control at all on a v2 voice, because a tag would be spoken', async () => {
        renderWithModel('eleven_multilingual_v2')
        const user = userEvent.setup()

        await user.click(screen.getByRole('button', { name: /edit/i }))

        // Every door onto a tag: the palette that inserts one, the delivery lever that adds them,
        // and the mode switch that would reach it.
        expect(
            screen.queryByRole('button', { name: /add tone/i })
        ).not.toBeInTheDocument()
        expect(
            screen.queryByLabelText(/describe the read you want/i)
        ).not.toBeInTheDocument()
        expect(
            screen.queryByRole('radio', { name: 'Delivery' })
        ).not.toBeInTheDocument()

        // The CONTENT rewrite is not a tone lever and stays available: it returns plain words and
        // the backend strips any tag the model volunteers.
        expect(
            screen.getByLabelText(
                'Instruction for rewriting the script content'
            )
        ).toBeInTheDocument()
    })

    it('says why, naming the model, instead of silently hiding the feature', async () => {
        renderWithModel('eleven_multilingual_v2')
        const user = userEvent.setup()

        await user.click(screen.getByRole('button', { name: /edit/i }))

        // The reason rides on the icon's accessible name, so it is available
        // to a screen reader without a paragraph of prose in the toolbar.
        expect(
            screen.getByRole('img', {
                name: /eleven_multilingual_v2.*cannot use delivery tones/i,
            })
        ).toBeInTheDocument()
    })

    it('fails closed on an unknown model rather than offering tone', async () => {
        renderWithModel('some_future_model')
        const user = userEvent.setup()

        await user.click(screen.getByRole('button', { name: /edit/i }))

        expect(
            screen.queryByLabelText(/describe the read you want/i)
        ).not.toBeInTheDocument()
    })

    it('still offers tone on a v3 voice', async () => {
        renderWithModel('eleven_v3')
        const user = userEvent.setup()

        await user.click(screen.getByRole('button', { name: /edit/i }))

        expect(
            screen.getByLabelText(/describe the read you want/i)
        ).toBeInTheDocument()
        expect(
            screen.queryByRole('img', { name: /cannot use delivery tones/i })
        ).not.toBeInTheDocument()
    })
})

import { describe, expect, it } from 'vitest'
import { JobSchema } from './schemas'
import { JOB_FIXTURES } from '@/lib/api/mock/fixtures'

function fixture(jobId: string) {
    const found = JOB_FIXTURES.find((f) => f.JobId === jobId)
    if (!found) throw new Error(`fixture ${jobId} missing`)
    return found
}

/**
 * `JobLogLevel` on the backend is `{ Info = 0, Warn = 1, Error = 2 }` and the
 * response serialiser emits it as a bare int. Reading 0 as DEBUG shifted every
 * line down one severity: WARN arrived as INFO, so `isFlaggedLog` stopped
 * flagging the pre-review warning an operator must act on, and ERROR was
 * unreachable. Mock fixtures carried the same off-by-one, so only the deployed
 * app showed it.
 */
describe('JobSchema — log level wire contract', () => {
    const base = fixture('arn-job-00007')

    it('maps the backend enum ordinals to their own severities', () => {
        const job = JobSchema.parse({
            ...base,
            Logs: [
                { At: base.CreatedAt, Level: 0, Message: 'info' },
                { At: base.CreatedAt, Level: 1, Message: 'warn' },
                { At: base.CreatedAt, Level: 2, Message: 'error' },
            ],
        })
        expect(job.logs.map((l) => l.level)).toEqual(['INFO', 'WARN', 'ERROR'])
    })

    it('treats an unknown ordinal as INFO rather than inventing a severity', () => {
        const job = JobSchema.parse({
            ...base,
            Logs: [{ At: base.CreatedAt, Level: 9, Message: 'future' }],
        })
        expect(job.logs[0]?.level).toBe('INFO')
    })
})

/**
 * The union declares the decision record non-optional on each terminal variant
 * and `job-hooks` narrows with `parse(data) as Job`. The parse must therefore
 * prove it: a terminal payload with no decision record has to fail HERE, named,
 * rather than reach a component that reads `job.rejection.reason`.
 */
describe('JobSchema — terminal variant invariants', () => {
    const rejected = fixture('arn-job-00010')

    it('accepts a rejected job that carries its rejection record', () => {
        expect(() => JobSchema.parse(rejected)).not.toThrow()
    })

    it('refuses a REJECTED payload with no rejection record', () => {
        const result = JobSchema.safeParse({ ...rejected, Rejection: null })
        expect(result.success).toBe(false)
        if (result.success) return
        const issue = result.error.issues.find((i) =>
            i.path.includes('rejection')
        )
        expect(issue?.message).toContain('requires rejection data')
    })

    it('accepts a missed job that carries its missed record', () => {
        // Built from the REJECTED fixture: MISSED requires the same pipeline
        // artifacts (traffic, incidents, script, audio) plus the missed record.
        expect(() =>
            JobSchema.parse({
                ...rejected,
                Status: 'MISSED',
                Missed: {
                    DeadlineAt: rejected.CreatedAt,
                    At: rejected.LastUpdatedAt,
                },
            })
        ).not.toThrow()
    })

    it('refuses a MISSED payload with no missed record', () => {
        const result = JobSchema.safeParse({
            ...rejected,
            Status: 'MISSED',
            Missed: null,
        })
        expect(result.success).toBe(false)
        if (result.success) return
        const issue = result.error.issues.find((i) => i.path.includes('missed'))
        expect(issue?.message).toContain('requires missed data')
    })

    it('refuses terminal payloads missing approval, publish or fallback', () => {
        const cases: readonly [string, Record<string, unknown>, string][] = [
            ['APPROVED', { Approval: null }, 'approval'],
            ['PUBLISHED', { Approval: null }, 'approval'],
            ['PUBLISHED', { Publish: null }, 'publish'],
            ['FALLBACK_USED', { Fallback: null }, 'fallback'],
        ]
        for (const [status, missing, field] of cases) {
            const source = JOB_FIXTURES.find((f) => f.Status === status)
            if (!source) throw new Error(`no ${status} fixture`)
            const result = JobSchema.safeParse({ ...source, ...missing })
            expect(result.success, `${status} without ${field}`).toBe(false)
        }
    })
})

import { describe, it, expect } from 'vitest'
import {
    SafetyTermSchema,
    SafetyTermListSchema,
    SAFETY_KINDS,
    SAFETY_SCOPES,
} from './schemas'

describe('SafetyTermSchema', () => {
    it('parses a term and camelizes PascalCase keys', () => {
        const parsed = SafetyTermSchema.parse({
            Id: 'st-1',
            Kind: 'Banned',
            Scope: 'Global',
            ScopeId: '',
            Term: 'gridlock',
            Note: 'avoid alarmist language',
            CreatedBy: 'admin@arn.com.au',
            CreatedAtUtc: '2026-01-01T00:00:00Z',
            UpdatedAtUtc: '2026-01-01T00:00:00Z',
        })
        expect(parsed.id).toBe('st-1')
        expect(parsed.kind).toBe('Banned')
        expect(parsed.scope).toBe('Global')
        expect(parsed.scopeId).toBe('')
        expect(parsed.note).toBe('avoid alarmist language')
    })

    it('tolerates a missing note', () => {
        const parsed = SafetyTermSchema.parse({
            Id: 'st-2',
            Kind: 'Allowed',
            Scope: 'Market',
            ScopeId: 'syd-1',
            Term: 'Gun Club Road',
            CreatedBy: 'admin@arn.com.au',
            CreatedAtUtc: '2026-01-01T00:00:00Z',
            UpdatedAtUtc: '2026-01-01T00:00:00Z',
        })
        expect(parsed.note == null).toBe(true)
        expect(parsed.scope).toBe('Market')
        expect(parsed.scopeId).toBe('syd-1')
    })

    it('parses an empty list', () => {
        expect(SafetyTermListSchema.parse([])).toEqual([])
    })

    it('exposes the kind and scope option constants', () => {
        expect(SAFETY_KINDS).toContain('Banned')
        expect(SAFETY_KINDS).toContain('Allowed')
        expect(SAFETY_SCOPES).toContain('Global')
        expect(SAFETY_SCOPES).toContain('Market')
    })
})

import { describe, expect, it } from 'vitest'
import { hasAdminRole, hasAppAccess, hasOperatorRole } from './use-current-user'

describe('frontend app-role helpers', () => {
    it('does not treat SWA pseudo-roles as an explicit app role', () => {
        const roles = ['anonymous', 'authenticated']

        expect(hasOperatorRole(roles)).toBe(false)
        expect(hasAdminRole(roles)).toBe(false)
    })

    it('recognises Operator and Admin case-insensitively', () => {
        expect(hasOperatorRole(['Operator'])).toBe(true)
        expect(hasOperatorRole(['operator'])).toBe(true)
        expect(hasAdminRole(['Admin'])).toBe(true)
        expect(hasAdminRole(['administrator'])).toBe(true)
    })

    it('treats either Operator or Admin as app access', () => {
        expect(hasAppAccess(['Operator'])).toBe(true)
        expect(hasAppAccess(['Admin'])).toBe(true)
    })

    it('passes through when SWA is authenticated but carries no custom role', () => {
        // dev's built-in provider, or a test/prod caller with no app role
        // assigned yet — the backend is the sole access authority here.
        expect(hasAppAccess(['anonymous', 'authenticated'])).toBe(true)
        expect(hasAppAccess(['authenticated'])).toBe(true)
    })

    it('denies access when there is no authenticated pseudo-role at all', () => {
        // signed-out, or a failed/empty /.auth/me response
        expect(hasAppAccess([])).toBe(false)
        expect(hasAppAccess(['anonymous'])).toBe(false)
    })
})

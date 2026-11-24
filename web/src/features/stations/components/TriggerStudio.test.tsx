// @vitest-environment jsdom
import { describe, expect, it } from 'vitest'
import { STEP_FIELDS, triggerSchema } from './trigger-studio-helpers'

/**
 * STEP_FIELDS drives the per-step validity dots: a schema field missing from
 * the map silently never invalidates its step. These tests force every new
 * schema field to be assigned to a step before it can ship.
 */
describe('TriggerStudio STEP_FIELDS', () => {
    const schemaKeys = Object.keys(triggerSchema.shape).sort()
    const mappedKeys = [...new Set(Object.values(STEP_FIELDS).flat())].sort()

    it('covers every field of triggerSchema', () => {
        const unassigned = schemaKeys.filter(
            (k) => !mappedKeys.includes(k as (typeof mappedKeys)[number])
        )
        expect(
            unassigned,
            'schema fields not assigned to any studio step — add them to STEP_FIELDS'
        ).toEqual([])
    })

    it('maps no field that the schema does not define', () => {
        const phantom = mappedKeys.filter(
            (k) => !schemaKeys.includes(k as string)
        )
        expect(
            phantom,
            'STEP_FIELDS references fields missing from triggerSchema'
        ).toEqual([])
    })

    it('assigns each field to at most one step', () => {
        const all = Object.values(STEP_FIELDS).flat()
        const duplicates = all.filter((k, i) => all.indexOf(k) !== i)
        expect(duplicates).toEqual([])
    })
})

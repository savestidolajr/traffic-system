/**
 * Utilities for mapping API requests and responses.
 */

/**
 * Shallow mapped type that converts PascalCase keys to camelCase via Uncapitalize.
 * Preserves index signatures from Zod .passthrough() by only uncapitalizing
 * known (literal) string keys, not the general `string` index.
 */
type CamelizeKeys<T extends Record<string, unknown>> = {
    [
        K in keyof T as K extends string
            ? string extends K
                ? K
                : Uncapitalize<K>
            : K
    ]: T[K]
}

/**
 * Converts a PascalCase string to camelCase.
 */
function toCamelCase(str: string): string {
    return str.charAt(0).toLowerCase() + str.slice(1)
}

/**
 * Converts a camelCase string to PascalCase.
 */
function toPascalCase(str: string): string {
    return str.charAt(0).toUpperCase() + str.slice(1)
}

/**
 * Transforms keys of an object from PascalCase to camelCase.
 * Returns CamelizeKeys<T> so Zod schemas preserve inferred types through transforms.
 */
export function camelizeKeys<T extends Record<string, unknown>>(
    obj: T
): CamelizeKeys<T> {
    if (!obj || typeof obj !== 'object' || Array.isArray(obj))
        return obj as CamelizeKeys<T>

    return Object.keys(obj).reduce(
        (acc, key) => {
            const camelKey = toCamelCase(key)
            let value = obj[key]

            // Recursive for nested objects and arrays
            if (Array.isArray(value)) {
                value = value.map((item) =>
                    item && typeof item === 'object'
                        ? camelizeKeys(item as Record<string, unknown>)
                        : item
                )
            } else if (value && typeof value === 'object') {
                value = camelizeKeys(value as Record<string, unknown>)
            }

            acc[camelKey] = value
            return acc
        },
        {} as Record<string, unknown>
    ) as CamelizeKeys<T>
}

/**
 * Shallow mapped type that converts camelCase keys to PascalCase via Capitalize.
 * Mirrors CamelizeKeys<T> so callers keep their inferred shape without casting.
 */
type PascalizeKeys<T extends object> = {
    [
        K in keyof T as K extends string
            ? string extends K
                ? K
                : Capitalize<K>
            : K
    ]: T[K]
}

/**
 * Transforms keys of an object from camelCase to PascalCase.
 * Returns PascalizeKeys<T> so call sites don't need `as unknown as Record<...>`.
 */
export function pascalizeKeys<T extends object>(obj: T): PascalizeKeys<T> {
    const rec = obj as Record<string, unknown>
    if (!rec || typeof rec !== 'object' || Array.isArray(rec))
        return obj as unknown as PascalizeKeys<T>

    return Object.keys(rec).reduce(
        (acc, key) => {
            const pascalKey = toPascalCase(key)
            let value = rec[key]

            // Recursive for nested objects and arrays
            if (Array.isArray(value)) {
                value = value.map((item) =>
                    item && typeof item === 'object'
                        ? pascalizeKeys(item as Record<string, unknown>)
                        : item
                )
            } else if (value && typeof value === 'object') {
                value = pascalizeKeys(value as Record<string, unknown>)
            }

            acc[pascalKey] = value
            return acc
        },
        {} as Record<string, unknown>
    ) as PascalizeKeys<T>
}

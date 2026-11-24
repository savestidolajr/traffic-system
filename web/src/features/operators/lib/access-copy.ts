/**
 * Access-state notices shared by the card and table views of the operator
 * directory. The two views toggle, so the same fact must read identically
 * in both without duplicating the literals.
 */

/** Entra read OK, but the directory no longer returns this person. */
export const RETAINED_ROW_NOTICE =
    'Not returned by the latest Entra read. Kept from a previous sign-in; verify an Entra application role is still assigned.'

/** Directory unreadable; the role shown is from the person's own token. */
export const SIGN_IN_ROLE_NOTICE =
    'Entra is unreadable. The role shown is from their last sign-in.'

/** Directory unreadable and no role on record for this row. */
export const ROLE_UNKNOWN_NOTICE =
    'Entra is unreadable, so the role is unknown. This row is from a previous sign-in.'

/** Directory read OK, but the entry carries no application role. */
export const MISCONFIGURED_NOTICE =
    'No application role is assigned in Entra. Assign Operator or Admin to grant access.'

/**
 * Humanize a directory source for "From …" copy. Entra emits
 * "Group:APP-IBP-Test-Admin" (no space after the colon); render the group
 * qualifier with a separator so "From Group · APP-IBP-Test-Admin" reads like
 * "From Direct" / "From Assignment".
 */
export const formatSource = (source: string): string =>
    source.startsWith('Group:')
        ? `Group \u00B7 ${source.slice('Group:'.length)}`
        : source

import { apiClient } from '@/lib/api/client'
import { createQueryKeys } from '@/lib/api/create-query-keys'
import { useQueryFactory } from '@/lib/api/use-query-factory'
import { useMutationFactory } from '@/lib/api/use-mutation-factory'
import { OPERATORS, OPERATORS_ME, operatorAssignment } from '@/lib/api/paths'
import {
    OperatorScopeSchema,
    OperatorDirectorySchema,
    OperatorAssignmentSchema,
    type OperatorScope,
    type OperatorDirectory,
    type OperatorAssignment,
} from './schemas'

const OPERATOR_QUERY_KEYS = createQueryKeys('operators')
const SCOPE_KEY = [...OPERATOR_QUERY_KEYS.all, 'me'] as const

// API CALLS

const fetchMyScope = async (): Promise<OperatorScope> => {
    const { data } = await apiClient.get(OPERATORS_ME)
    return OperatorScopeSchema.parse(data)
}

const fetchOperators = async (): Promise<OperatorDirectory> => {
    const { data } = await apiClient.get(OPERATORS)
    return OperatorDirectorySchema.parse(data)
}

const upsertAssignment = async ({
    objectId,
    markets,
}: {
    objectId: string
    markets: string[]
}): Promise<OperatorAssignment> => {
    const { data } = await apiClient.put(operatorAssignment(objectId), {
        markets,
    })
    return OperatorAssignmentSchema.parse(data)
}

// HOOKS

/**
 * The current operator's market scope. Drives scope-aware partitioning on the
 * workstation — a scoped operator sees out-of-scope stations demoted, and the
 * backend 403s out-of-scope decisions regardless. Cached aggressively: scope
 * changes are rare admin actions.
 */
export const useOperatorScope = () =>
    useQueryFactory(SCOPE_KEY, fetchMyScope, {
        staleTime: 1000 * 60 * 5,
    })
/** Live Entra role holders merged with persisted market assignments. */
export const useOperatorAssignments = (options?: { enabled?: boolean }) =>
    useQueryFactory(OPERATOR_QUERY_KEYS.lists(), fetchOperators, {
        enabled: options?.enabled ?? true,
    })

export const useUpsertOperatorAssignment = () =>
    useMutationFactory(upsertAssignment, () => [OPERATOR_QUERY_KEYS.all])

import React from 'react'
import { Separator } from '@/components/ui/separator'
import { Flex } from '@/components/ui/flex'

interface HeaderProps {
    title: string
    description: string
    /**
     * Rendered INLINE after the h1, not in the right-hand action slot. For a
     * control that scopes the whole page and reads as part of its title —
     * "Bulletin rules for [All stations]". A page-level scope is a statement
     * about WHAT YOU ARE LOOKING AT, so it belongs beside the name of the
     * thing, not floated to the opposite corner among the actions.
     */
    titleAfter?: React.ReactNode
    children?: React.ReactNode
}

export function Header({
    title,
    description,
    titleAfter,
    children,
}: HeaderProps) {
    return (
        <Flex direction="col" gap="4" className="mb-4">
            <Flex
                direction="col"
                gap="2"
                className="sm:flex-row sm:items-center sm:justify-between"
            >
                <div className="flex flex-col gap-1.5">
                    <div className="flex flex-wrap items-center gap-3">
                        <h1 className="text-3xl font-display font-bold tracking-tight text-foreground">
                            {title}
                        </h1>
                        {titleAfter}
                    </div>
                    <p className="text-muted-foreground">{description}</p>
                </div>
                {children && (
                    <Flex align="center" gap="3">
                        {children}
                    </Flex>
                )}
            </Flex>
            <Separator />
        </Flex>
    )
}

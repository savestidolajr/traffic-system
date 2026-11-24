import * as React from 'react'

import { cn } from '@/lib/utils'

interface FlexProps extends React.ComponentProps<'div'> {
    direction?: 'row' | 'col'
    align?: 'start' | 'center' | 'end' | 'stretch'
    justify?: 'start' | 'center' | 'end' | 'between' | 'around' | 'evenly'
    gap?: '1' | '2' | '3' | '4' | '6' | '8' | '12' | '16'
    wrap?: boolean
}

function Flex({
    className,
    direction = 'row',
    align,
    justify,
    gap,
    wrap,
    ...props
}: FlexProps) {
    return (
        <div
            className={cn(
                'flex',
                {
                    'flex-row': direction === 'row',
                    'flex-col': direction === 'col',
                    'items-start': align === 'start',
                    'items-center': align === 'center',
                    'items-end': align === 'end',
                    'items-stretch': align === 'stretch',
                    'justify-start': justify === 'start',
                    'justify-center': justify === 'center',
                    'justify-end': justify === 'end',
                    'justify-between': justify === 'between',
                    'justify-around': justify === 'around',
                    'justify-evenly': justify === 'evenly',
                    'gap-1': gap === '1',
                    'gap-2': gap === '2',
                    'gap-3': gap === '3',
                    'gap-4': gap === '4',
                    'gap-6': gap === '6',
                    'gap-8': gap === '8',
                    'gap-12': gap === '12',
                    'gap-16': gap === '16',
                    'flex-wrap': wrap,
                },
                className
            )}
            {...props}
        />
    )
}

export { Flex }

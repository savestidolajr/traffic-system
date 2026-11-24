import React from 'react'
import { Container } from '@/components/ui/container'

interface PageContainerProps {
    children: React.ReactNode
}

export function PageContainer({ children }: PageContainerProps) {
    return (
        <Container className="py-4 animate-in fade-in-0 slide-in-from-bottom-2 duration-150">
            {children}
        </Container>
    )
}

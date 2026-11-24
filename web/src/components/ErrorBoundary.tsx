import React, { Component, ReactNode } from 'react'
import { Link } from 'react-router'
import { Alert, AlertTitle, AlertDescription } from '@/components/ui/alert'
import { Button } from '@/components/ui/button'

interface Props {
    children: ReactNode
}

interface State {
    hasError: boolean
    error?: Error
}

class ErrorBoundary extends Component<Props, State> {
    constructor(props: Props) {
        super(props)
        this.state = { hasError: false }
    }

    static getDerivedStateFromError(error: Error): State {
        return { hasError: true, error }
    }

    componentDidCatch(error: Error, errorInfo: React.ErrorInfo) {
        console.error('Error caught by boundary:', error, errorInfo)
    }

    handleReload = () => {
        window.location.reload()
    }

    render() {
        if (this.state.hasError) {
            return (
                <div className="min-h-screen flex items-center justify-center p-4">
                    <Alert variant="destructive" className="max-w-md">
                        <AlertTitle>Something went wrong</AlertTitle>
                        <AlertDescription className="mt-2">
                            {this.state.error?.message ||
                                'An unexpected error occurred.'}
                        </AlertDescription>
                        <div className="flex gap-2 mt-4">
                            <Button onClick={this.handleReload}>
                                Reload Page
                            </Button>
                            <Link to="/">
                                <Button variant="outline">
                                    Go to Dashboard
                                </Button>
                            </Link>
                        </div>
                    </Alert>
                </div>
            )
        }

        return this.props.children
    }
}

export default ErrorBoundary

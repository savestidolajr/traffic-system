import { lazy, Suspense, type ReactNode } from 'react'
import { Loader2, LogIn, ShieldCheck } from 'lucide-react'
import {
    BrowserRouter,
    Routes,
    Route,
    Navigate,
    useLocation,
} from 'react-router'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { ThemeProvider } from 'next-themes'
import { Toaster } from 'sonner'
import {
    QUERY_STALE_TIME_MS,
    QUERY_RETRY_DELAY_CAP_MS,
} from '@/lib/constants/timing'
import { isPermanentClientError } from '@/lib/api/client'
import {
    canAccessRequiredRole,
    ROUTE_JOBS,
    ROUTE_DESK,
    ROUTE_HISTORY,
    ROUTE_STATIONS,
    ROUTE_SCHEDULES,
    ROUTE_TRIGGER,
    ROUTE_OPERATORS,
    ROUTE_VOICES,
    ROUTE_GUIDANCE,
    ROUTE_DELIVERY,
    ROUTE_BOOKENDS,
    ROUTE_PRONUNCIATION,
    ROUTE_CONFIG_TRANSFER,
    ROUTE_ACTIVITY,
    type RequiredRole,
} from '@/lib/routes'
import { NavigationBar } from '@/components/NavigationBar'
import { AppSidebar } from '@/components/navigation/AppSidebar'
import { DotGridBackdrop } from '@/components/ui/aceternity/dot-grid-backdrop'
import { AlarmEffects } from '@/features/notifications'
import { CommandPalette } from '@/components/CommandPalette'
import { TooltipProvider } from '@/components/ui/tooltip'
import { Button } from '@/components/ui/button'
import { Placeholder } from '@/components/Placeholder'
import {
    useCurrentUser,
    useHasAppAccess,
    useIsAdmin,
    useIsAuthenticated,
    useIsOperator,
} from '@/hooks/use-current-user'
import ErrorBoundary from '@/components/ErrorBoundary'
const HomePage = lazy(() =>
    import('./pages/HomePage').then((m) => ({ default: m.HomePage }))
)
const OnAirPage = lazy(() =>
    import('./pages/OnAirPage').then((m) => ({ default: m.OnAirPage }))
)
const DeskPage = lazy(() => import('./pages/DeskPage'))
const HistoryPage = lazy(() =>
    import('./pages/HistoryPage').then((m) => ({ default: m.HistoryPage }))
)
const StationsPage = lazy(() =>
    import('./pages/StationsPage').then((m) => ({ default: m.StationsPage }))
)
const OperatorsPage = lazy(() =>
    import('./pages/OperatorsPage').then((m) => ({ default: m.OperatorsPage }))
)
const VoicesPage = lazy(() =>
    import('./pages/VoicesPage').then((m) => ({ default: m.VoicesPage }))
)
const DeliveryPage = lazy(() =>
    import('./pages/DeliveryPage').then((m) => ({ default: m.DeliveryPage }))
)
const BookendsPage = lazy(() =>
    import('./pages/BookendsPage').then((m) => ({ default: m.BookendsPage }))
)
const PronunciationPage = lazy(() =>
    import('./pages/PronunciationPage').then((m) => ({
        default: m.PronunciationPage,
    }))
)
const GuidancePage = lazy(() =>
    import('./pages/GuidancePage').then((m) => ({ default: m.GuidancePage }))
)
const ConfigTransferPage = lazy(() =>
    import('./pages/ConfigTransferPage').then((m) => ({
        default: m.ConfigTransferPage,
    }))
)
const ActivityPage = lazy(() =>
    import('./pages/ActivityPage').then((m) => ({ default: m.ActivityPage }))
)
const NotFoundPage = lazy(() =>
    import('./pages/NotFoundPage').then((m) => ({ default: m.NotFoundPage }))
)

const queryClient = new QueryClient({
    defaultOptions: {
        queries: {
            staleTime: QUERY_STALE_TIME_MS,
            retry: (failureCount, error) =>
                isPermanentClientError(error) ? false : failureCount < 3,
            retryDelay: (attemptIndex) =>
                Math.min(1000 * 2 ** attemptIndex, QUERY_RETRY_DELAY_CAP_MS),
            refetchOnWindowFocus: false,
        },
        mutations: {
            // A bare `retry: 1` retried EVERY failure, including permanent 4xx.
            // That is actively harmful on an irreversible action: an approve
            // whose response was lost got retried with the same now-stale
            // etag, the server refused with 412, and the operator was told the
            // approval failed when it had in fact landed. The queries branch
            // above already refused 4xx retries; mutations never got the same
            // treatment.
            retry: (failureCount, error) =>
                isPermanentClientError(error) ? false : failureCount < 1,
        },
    },
})

function LoadingScreen() {
    return (
        <div className="flex h-dvh items-center justify-center gap-2 bg-background p-8 text-sm text-muted-foreground">
            <Loader2 className="size-4 animate-spin" />
            Loading…
        </div>
    )
}

function NoAppAccess() {
    const isAuthenticated = useIsAuthenticated()

    if (!isAuthenticated) {
        return (
            <div className="relative flex h-dvh items-center justify-center overflow-hidden bg-background p-6">
                <DotGridBackdrop />
                <div className="relative w-full max-w-xl">
                    <Placeholder
                        icon={LogIn}
                        message="Sign in required"
                        action={
                            <Button asChild>
                                <a href="/.auth/login/aad?post_login_redirect_uri=/">
                                    Sign in
                                </a>
                            </Button>
                        }
                    />
                </div>
            </div>
        )
    }

    return (
        <div className="relative flex h-dvh items-center justify-center overflow-hidden bg-background p-6">
            <DotGridBackdrop />
            <div className="relative w-full max-w-xl">
                <Placeholder
                    icon={ShieldCheck}
                    message="Access not assigned"
                    action={
                        <div className="flex flex-col items-center gap-3">
                            <p className="max-w-md text-xs text-muted-foreground">
                                Ask an ARN administrator to add the required app
                                role, then sign out and back in.
                            </p>
                            <Button variant="outline" asChild>
                                <a href="/.auth/logout?post_logout_redirect_uri=/">
                                    Sign out
                                </a>
                            </Button>
                        </div>
                    }
                />
            </div>
        </div>
    )
}

function AccessDeniedSection({
    requiredRole,
}: {
    readonly requiredRole: RequiredRole
}) {
    return (
        <div className="p-6">
            <Placeholder
                icon={ShieldCheck}
                message={`${requiredRole === 'admin' ? 'Admin' : 'Operator'} access required`}
                variant="error"
            />
        </div>
    )
}

function ProtectedRoute({
    requiredRole,
    children,
}: {
    readonly requiredRole: RequiredRole
    readonly children: ReactNode
}) {
    const isAdmin = useIsAdmin()
    const isOperator = useIsOperator()
    if (!canAccessRequiredRole(requiredRole, { isAdmin, isOperator })) {
        return <AccessDeniedSection requiredRole={requiredRole} />
    }
    return children
}

function AppRoutes() {
    const location = useLocation()
    return (
        <ErrorBoundary key={location.pathname}>
            <Routes>
                {/* The front door: SaaS-style hero with live numbers */}
                <Route
                    path="/"
                    element={
                        <ProtectedRoute requiredRole="operator">
                            <HomePage />
                        </ProtectedRoute>
                    }
                />
                <Route
                    path={ROUTE_DESK}
                    element={
                        <ProtectedRoute requiredRole="operator">
                            <DeskPage />
                        </ProtectedRoute>
                    }
                />
                {/* The operator workstation; /jobs/:jobId deep-links a selection */}
                <Route
                    path={ROUTE_JOBS}
                    element={
                        <ProtectedRoute requiredRole="operator">
                            <OnAirPage />
                        </ProtectedRoute>
                    }
                />
                <Route
                    path={`${ROUTE_JOBS}/:jobId`}
                    element={
                        <ProtectedRoute requiredRole="operator">
                            <OnAirPage />
                        </ProtectedRoute>
                    }
                />
                <Route
                    path={ROUTE_HISTORY}
                    element={
                        <ProtectedRoute requiredRole="operator">
                            <HistoryPage />
                        </ProtectedRoute>
                    }
                />
                <Route
                    path={ROUTE_OPERATORS}
                    element={
                        <ProtectedRoute requiredRole="admin">
                            <OperatorsPage />
                        </ProtectedRoute>
                    }
                />
                <Route
                    path={ROUTE_STATIONS}
                    element={
                        <ProtectedRoute requiredRole="operator">
                            <StationsPage />
                        </ProtectedRoute>
                    }
                />
                {/* Voices is operator-reachable AND operator-writable: operators curate
                    the voice library. Every voice-profile route except DELETE resolves to
                    Operator in the backend's route matrix; the page gates Delete alone. */}
                <Route
                    path={ROUTE_VOICES}
                    element={
                        <ProtectedRoute requiredRole="operator">
                            <VoicesPage />
                        </ProtectedRoute>
                    }
                />
                <Route
                    path={ROUTE_DELIVERY}
                    element={
                        <ProtectedRoute requiredRole="operator">
                            <DeliveryPage />
                        </ProtectedRoute>
                    }
                />
                {/* Bookends is operator-reachable AND operator-writable: a pair is the
                    station's own opening and closing lines. Every bookend route except
                    DELETE resolves to Operator in the backend's route matrix; the gallery
                    gates the Delete affordance alone. */}
                <Route
                    path={ROUTE_BOOKENDS}
                    element={
                        <ProtectedRoute requiredRole="operator">
                            <BookendsPage />
                        </ProtectedRoute>
                    }
                />
                <Route
                    path={ROUTE_PRONUNCIATION}
                    element={
                        <ProtectedRoute requiredRole="operator">
                            <PronunciationPage />
                        </ProtectedRoute>
                    }
                />
                <Route
                    path={ROUTE_GUIDANCE}
                    element={
                        <ProtectedRoute requiredRole="operator">
                            <GuidancePage />
                        </ProtectedRoute>
                    }
                />
                <Route
                    path={ROUTE_SCHEDULES}
                    element={<Navigate to={ROUTE_STATIONS} replace />}
                />
                <Route
                    path={ROUTE_TRIGGER}
                    element={<Navigate to={ROUTE_STATIONS} replace />}
                />
                <Route
                    path={ROUTE_CONFIG_TRANSFER}
                    element={
                        <ProtectedRoute requiredRole="admin">
                            <ConfigTransferPage />
                        </ProtectedRoute>
                    }
                />
                <Route
                    path={ROUTE_ACTIVITY}
                    element={
                        <ProtectedRoute requiredRole="operator">
                            <ActivityPage />
                        </ProtectedRoute>
                    }
                />
                <Route path="*" element={<NotFoundPage />} />
            </Routes>
        </ErrorBoundary>
    )
}

function AppFrame() {
    const { isLoading } = useCurrentUser()
    const hasAppAccess = useHasAppAccess()

    if (isLoading) return <LoadingScreen />
    if (!hasAppAccess) return <NoAppAccess />

    return (
        <div className="flex h-dvh flex-col bg-background md:flex-row">
            <a
                href="#main-content"
                className="sr-only focus:not-sr-only focus:fixed focus:top-4 focus:left-4 focus:px-4 focus:py-2 focus:bg-background focus:text-foreground focus:rounded-md focus:shadow-lg focus:ring-2 focus:ring-ring"
            >
                Skip to main content
            </a>
            <AppSidebar />
            <div className="relative flex min-w-0 flex-1 flex-col">
                <DotGridBackdrop />
                <NavigationBar />
                <main
                    id="main-content"
                    className="relative min-h-0 flex-1 overflow-y-auto overflow-x-hidden"
                >
                    <Suspense
                        fallback={
                            <div className="flex h-full items-center justify-center gap-2 p-8 text-sm text-muted-foreground">
                                <Loader2 className="size-4 animate-spin" />
                                Loading…
                            </div>
                        }
                    >
                        <AppRoutes />
                    </Suspense>
                </main>
            </div>
            <AlarmEffects />
            <CommandPalette />
        </div>
    )
}

function App() {
    return (
        <ThemeProvider attribute="class" defaultTheme="dark" enableSystem>
            <QueryClientProvider client={queryClient}>
                <TooltipProvider>
                    <BrowserRouter>
                        <AppFrame />
                        <Toaster richColors position="bottom-right" />
                    </BrowserRouter>
                </TooltipProvider>
            </QueryClientProvider>
        </ThemeProvider>
    )
}

export default App

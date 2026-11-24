/**
 * Generic icon SSOT.
 *
 * Job status icons live in features/jobs/config/status-config.ts (STATUS_CONFIG).
 * Never import a lucide icon directly for a concept named here — pull it from ICON.
 *
 * Deliberate distinctions (do not collapse):
 *   - RadioTower (station / broadcast / on-air) vs Radio (the HERE traffic-data
 *     signal pipeline stage, kept in STATUS_CONFIG.HERE_FETCHED).
 *   - AlertTriangle (warning / failure) vs XCircle (human rejection) vs
 *     AlertCircle (error-boundary message-level alert).
 *   - Mic2 (voice identity) vs Volume2 (audio output) vs AudioLines (synthesizing,
 *     kept in STATUS_CONFIG.SYNTHESIZING_AUDIO).
 */
import {
    Activity,
    AlertCircle,
    AlertTriangle,
    Archive,
    ArchiveRestore,
    ArrowRight,
    ArrowUp,
    Building2,
    ChevronDown,
    ChevronUp,
    CircleCheck,
    Clock,
    CloudSun,
    Copy,
    CornerDownRight,
    Download,
    FileJson,
    Eye,
    Fuel,
    Gauge,
    HelpCircle,
    History,
    ListChecks,
    MapPin,
    MessageSquareText,
    Mic2,
    MoreHorizontal,
    Pause,
    Pencil,
    Play,
    Plus,
    RadioTower,
    RefreshCw,
    Route,
    RotateCcw,
    Search,
    SearchX,
    Settings2,
    ShieldCheck,
    Sparkles,
    SlidersHorizontal,
    Timer,
    ThumbsUp,
    Trash2,
    Upload,
    Volume2,
    Wand2,
    X,
    XCircle,
    Zap,
    type LucideIcon,
} from 'lucide-react'

/** Semantic key → lucide glyph. One glyph per concept. */
export const ICON = {
    // Entity actions
    edit: Pencil,
    duplicate: Copy,
    delete: Trash2,
    view: Eye,
    more: MoreHorizontal,
    add: Plus, // UserPlus is the only sanctioned variant, for "add operator"
    archive: Archive,
    unarchive: ArchiveRestore,
    // Review decisions
    approve: ThumbsUp,
    reject: XCircle,
    rejectToFallback: CornerDownRight,
    regenerate: RefreshCw,
    revise: Wand2,
    reset: RotateCcw,
    close: X,
    // Search / disclosure / nav
    search: Search,
    noResults: SearchX,
    expand: ChevronDown,
    collapse: ChevronUp,
    forward: ArrowRight,
    send: ArrowUp,
    download: Download,
    upload: Upload,
    fileJson: FileJson,
    settings: Settings2,
    // Audio transport
    play: Play,
    pause: Pause,
    // Verdicts / alerts
    success: CircleCheck,
    warning: AlertTriangle,
    info: AlertCircle,
    // Content sources
    traffic: Route,
    weather: CloudSun,
    fuel: Fuel,
    incident: AlertTriangle,
    // Domain entities
    broadcast: RadioTower,
    history: History,
    market: MapPin,
    org: Building2,
    voice: Mic2,
    audio: Volume2,
    // Vitals
    airSlot: RadioTower,
    duration: Timer,
    coverage: ListChecks,
    freshness: Activity,
    // Qualities
    urgent: Zap,
    speed: Gauge,
    ai: Sparkles,
    safety: ShieldCheck,
    help: HelpCircle,
    tune: SlidersHorizontal,
    time: Clock,
    feedback: MessageSquareText,
} as const satisfies Record<string, LucideIcon>

/** Icon sizing tokens, paired with the type scale. */
export const ICON_SIZE = {
    /** 12px — pair with text-2xs / text-3xs */
    micro: 'size-3',
    /** 14px — pair with text-xs / text-sm */
    inline: 'size-3.5',
    /** 16px — buttons, standalone */
    action: 'size-4',
    /** 20px — dialog / header badges */
    badge: 'size-5',
} as const

/**
 * The only sanctioned raw-palette map: weather color is domain-semantic
 * (sun = amber, rain = sky), so it stays off the generic token ramp.
 * Keyed by WeatherTone ('sun' | 'cloud' | 'rain' | 'storm' | 'fog' | 'snow').
 */
export const WEATHER_TINT = {
    sun: 'text-amber-300',
    cloud: 'text-slate-300',
    rain: 'text-sky-300',
    storm: 'text-violet-300',
    fog: 'text-slate-400',
    snow: 'text-cyan-200',
} as const

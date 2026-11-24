namespace ArnBulletinGenerator.Domain.Errors;

/// <summary>
///     Centralized error messages for the ArnBulletinGenerator domain.
///     Use these constants for consistent error messaging across layers.
/// </summary>
public static class ErrorMessages
{
    public static class Configuration
    {
        public const string AudioApiKeyNotConfigured = "Audio API key is not configured";
    }

    public static class Http
    {
        public const string RequestBodyRequired = "Request body is required";
        public const string JobIdRequired = "Job ID is required.";
        public const string MarketIdRequired = "Market ID is required.";
    }

    public static class Job
    {
        public const string NotFound = "Job not found";

        public const string CannotRegenerateFromStatus =
            "Only rejected, failed, or fallback-covered jobs can be regenerated. Reject a waiting bulletin with feedback first, or retry a failed or fallback-covered one.";

        public const string OnlyWaitingCanEscalate = "Only jobs waiting for approval can be escalated";

        public const string OnlyWaitingCanBeApproved =
            "Cannot approve a job in status {0}: the bulletin has not finished generating and reached review yet.";

        /// <summary>
        ///     For statuses that HAVE finished and been decided (Approved, Rejected, Published,
        ///     Failed). Reusing the "not finished generating" wording for these told operators
        ///     something plainly untrue about a bulletin that had already aired.
        /// </summary>
        public const string CannotApproveSettled =
            "Cannot approve a job in status {0}: this bulletin has already been decided.";

        public const string MustBeApprovedToPublish = "Job must be approved before publishing";

        public const string CannotApproveFromStatus = "Cannot approve job from status: {0}";
        public const string CannotRejectFromStatus = "Can only reject jobs that are waiting for approval";

        public const string CannotMarkMissedFromStatus =
            "Only an unpublished, unsettled bulletin can be marked missed.";

        public const string CannotFailTerminalJob =
            "Cannot mark a job as failed when it is already in terminal state: {0}";

        public const string CannotApplyFallbackToTerminalJob = "Cannot apply fallback to a job in terminal state: {0}";
        public const string Conflict = "Job was modified by another writer; please retry.";

        public const string CorridorSelectionEmpty =
            "Invalid corridor selection: at least one corridor must be selected.";

        public const string CorridorSelectionUnknown =
            "Invalid corridor selection: no corridor on this job matches: {0}";

        public const string GoodRunSelectionUnknown =
            "Invalid good-run selection: no flowing road on this job matches: {0}";


        public const string EtagRequired =
            "A revision token is required for this action. Reload the bulletin and try again.";
        public const string AudioIntegrityFailed =
            "Delivery aborted: the audio no longer matches the approved revision (integrity check failed).";
        public const string RevisionMismatch =
            "The bulletin changed after you loaded it (a newer revision exists). Refresh and review the latest version before deciding.";

    }

    public static class AudioSynthesis
    {
        public const string UnknownDraftError = "Unknown audio synthesis draft error";
    }

    public static class Broadcast
    {
        /// <summary>
        ///     Stable prefix identifying a content-limited generation outcome: there was not enough
        ///     honest material to fill the slot within the slow-down cap. Callers match on this to
        ///     route to the fallback chain instead of treating it as a hard pipeline failure, and to
        ///     suppress queue redelivery (the same fetched content cannot fill the slot on retry).
        /// </summary>
        public const string ContentLimitedPrefix = "Content-limited:";
        /// <summary>
        ///     Stable prefix identifying a content-safety blocked outcome. The script was rejected
        ///     by the content-safety filter and cannot succeed on redelivery with the same content.
        /// </summary>
        public const string ContentSafetyViolationPrefix = "Content safety violation:";

        public static string ContentLimited(double spokenSeconds, double targetSeconds) =>
            $"Content-limited: best spoken {spokenSeconds:F1}s could not fill the {targetSeconds:F0}s slot within the slow-down cap.";
    }

    public static class Validation
    {
        public const string ScriptGenerationFailed = "Script generation failed: {0}";
        public const string SavingFailed = "Saving failed: {0}";
        public const string DurationActualTooFar = "Actual duration {0:F1}s too far from target (> {1}s)";
    }

    public static class Schedule
    {
        public const string NotFound = "Schedule not found";
    }

    public static class Guidance
    {
        public const string NotFound = "Guidance not found";
        public const string CandidateNotFound = "Candidate not found";
    }

    public static class VoiceSettings
    {
        public const string StabilityOutOfRange = "Stability must be between 0.0 and 1.0";
        public const string SimilarityBoostOutOfRange = "SimilarityBoost must be between 0.0 and 1.0";
        public const string StyleOutOfRange = "Style must be between 0.0 and 1.0";
        public const string SpeedOutOfRange = "Speed must be between 0.7 and 1.2";
    }

    public static class VoiceProfile
    {
        // Interpolated from the constant so the bound and the message it is rejected with can
        // never drift apart — the previous literal "40" outlived two changes to the cap.
        public static readonly string PaceBoostWpmOutOfRange =
            $"Delivery pace boost must be between 0 and {Models.VoiceProfile.MaxPaceBoostWpm:0} wpm.";
    }
}

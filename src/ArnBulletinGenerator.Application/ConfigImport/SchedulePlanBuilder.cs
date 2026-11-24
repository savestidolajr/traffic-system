using ArnBulletinGenerator.Application.Commands;
using ArnBulletinGenerator.Application.Mapping;
using ArnBulletinGenerator.Domain.Contracts;
using ArnBulletinGenerator.Domain.Models;
using ArnBulletinGenerator.Domain.Services;
using ArnBulletinGenerator.Application.ConfigTransfer;
using static ArnBulletinGenerator.Application.ConfigImport.ConfigImportHelpers;

namespace ArnBulletinGenerator.Application.ConfigImport;

/// <summary>Plans Create/Update/Unchanged/Error actions for the Schedules config section (keyed
/// by market id; libraries are validated first so referential checks below run off a complete
/// picture of what will exist once the plan applies).</summary>
internal static class SchedulePlanBuilder
{
    public static async Task<Result> PlanAsync(
        ConfigImportContext ctx,
        List<ConfigScheduleDto> dtos,
        HashSet<string> voiceProfileIds, HashSet<string> deliveryStyleIds, HashSet<string> playbookIds,
        HashSet<string> dictionaryIds, HashSet<string> globalDictionaryIds, HashSet<string> bookendIds,
        List<PlannedConfigItem> items, CancellationToken ct,
        IReadOnlyList<MarketSchedule>? existingSchedules = null)
    {
        var existingByMarket = (existingSchedules ?? []).ToDictionary(
            s => s.MarketId, StringComparer.OrdinalIgnoreCase);

        // Two entries for the same market would both plan against the existing schedule and the
        // second apply would upsert over the first — reject at plan time (same guard shape as
        // the per-market replace-list sections).
        var seenMarkets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var dto in dtos)
        {
            var marketId = dto.MarketId?.Trim() ?? string.Empty;
            var name = dto.Name ?? marketId;
            if (!StorageKey.IsValid(marketId))
            {
                items.Add(ErrorItem(ConfigSections.Schedules, marketId, name, "MarketId is not a valid identifier."));
                continue;
            }

            if (!seenMarkets.Add(marketId))
            {
                items.Add(ErrorItem(ConfigSections.Schedules, marketId, name,
                    "Each schedule market id may appear only once."));
                continue;
            }

            var request = dto.ToUpsertRequest();

            var coverage = Option<CoverageArea>.None;
            if (request.Coverage is not null)
            {
                var covResult = CoverageMapping.ToDomain(request.Coverage);
                if (covResult.IsFailure)
                {
                    items.Add(ErrorItem(ConfigSections.Schedules, marketId, name, covResult.Error));
                    continue;
                }

                // Same input-boundary rule as the interactive PUT: coverage outside the
                // operating region would silently fetch and air the wrong region's incidents.
                if (!CoverageArea.WithinOperatingRegion(covResult.Value))
                {
                    items.Add(ErrorItem(ConfigSections.Schedules, marketId, name,
                        $"Coverage must lie inside the platform's operating region "
                        + $"(lat {CoverageArea.MinOperatingLatitude}..{CoverageArea.MaxOperatingLatitude}, "
                        + $"lng {CoverageArea.MinOperatingLongitude}..{CoverageArea.MaxOperatingLongitude}); this area is outside it."));
                    continue;
                }

                coverage = Option<CoverageArea>.Some(covResult.Value);
            }

            var contentSelection = Option<BulletinContentSelection>.None;
            if (request.Content is not null)
            {
                var selection = request.Content.ToSelection();
                if (!selection.HasAnySource)
                {
                    items.Add(ErrorItem(ConfigSections.Schedules, marketId, name,
                        "At least one content source (traffic, weather, fuel) must be enabled."));
                    continue;
                }

                // Same required-implies-selected rule as the interactive PUT: a required flag on an
                // unselected source is inert, so importing it silently would leave the bundle
                // claiming a fixture the station will never air.
                if (selection.WeatherRequired && !selection.Weather)
                {
                    items.Add(ErrorItem(ConfigSections.Schedules, marketId, name,
                        "WeatherRequired is set but weather is not enabled as a content source."));
                    continue;
                }

                if (selection.FuelRequired && !selection.Fuel)
                {
                    items.Add(ErrorItem(ConfigSections.Schedules, marketId, name,
                        "FuelRequired is set but fuel is not enabled as a content source."));
                    continue;
                }

                contentSelection = Option<BulletinContentSelection>.Some(selection);
            }

            var danglingRef = ValidateReferences(
                request, voiceProfileIds, deliveryStyleIds, playbookIds, dictionaryIds, globalDictionaryIds, bookendIds);
            if (danglingRef is not null)
            {
                items.Add(ErrorItem(ConfigSections.Schedules, marketId, name, danglingRef));
                continue;
            }

            var rotation = Option<VoiceRotationPolicy>.None;
            if (request.VoiceRotation is not null)
            {
                var rotationResult = VoiceRotationMapping.ToDomain(request.VoiceRotation);
                if (rotationResult.IsFailure)
                {
                    items.Add(ErrorItem(ConfigSections.Schedules, marketId, name, rotationResult.Error));
                    continue;
                }

                var danglingMember = rotationResult.Value.AllReferencedVoiceIds()
                    .FirstOrDefault(id => !voiceProfileIds.Contains(id));
                if (danglingMember is not null)
                {
                    items.Add(ErrorItem(ConfigSections.Schedules, marketId, name,
                        $"Voice profile '{danglingMember}' does not exist (voice rotation)."));
                    continue;
                }

                rotation = Option<VoiceRotationPolicy>.Some(rotationResult.Value);
            }

            var bookendRotation = Option<BookendRotationPolicy>.None;
            if (request.BookendRotation is not null)
            {
                var rotationResult = BookendRotationMapping.ToDomain(request.BookendRotation);
                if (rotationResult.IsFailure)
                {
                    items.Add(ErrorItem(ConfigSections.Schedules, marketId, name, rotationResult.Error));
                    continue;
                }

                var danglingMember = rotationResult.Value.AllReferencedBookendIds()
                    .FirstOrDefault(id => !bookendIds.Contains(id));
                if (danglingMember is not null)
                {
                    items.Add(ErrorItem(ConfigSections.Schedules, marketId, name,
                        $"Bookend '{danglingMember}' does not exist (bookend rotation)."));
                    continue;
                }

                bookendRotation = Option<BookendRotationPolicy>.Some(rotationResult.Value);
            }

            var fillOrder = request.FillOrder is { Count: > 0 }
                ? Option<IReadOnlyList<FillSource>>.Some(
                    request.FillOrder.Select(ScheduleMappingExtensions.WireToFillSource).ToList())
                : Option<IReadOnlyList<FillSource>>.None;

            // Without this the import silently DROPPED the year calendar: the command
            // builders take it as an optional parameter, so omitting it here handed the
            // apply path Option.None and a promoted station arrived with every day type
            // and dated exception gone, under a green "Update". Absent in the bundle still
            // means "leave it alone"; present and malformed is an error, never a silent
            // partial write. Pattern cron validity is then checked by ScheduleService,
            // the same validator the interactive PUT uses.
            var calendar = Option<ScheduleCalendar>.None;
            if (request.Calendar is not null)
            {
                var calendarResult = request.Calendar.ToDomain();
                if (calendarResult.IsFailure)
                {
                    items.Add(ErrorItem(ConfigSections.Schedules, marketId, name,
                        calendarResult.Error));
                    continue;
                }

                calendar = Option<ScheduleCalendar>.Some(calendarResult.Value);
            }

            var updateCommand = ScheduleCommandBuilder.BuildUpdateCommand(
                marketId, request, coverage, contentSelection, rotation, fillOrder,
                bookendRotation, calendar);
            var createCommand = ScheduleCommandBuilder.BuildCreateCommand(
                marketId, request, coverage, contentSelection, rotation, fillOrder,
                bookendRotation, calendar);

            // Cron and timezone are checked FIRST, for both create and update. The apply path
            // (CreateOrUpdateAsync) rejects them, so a plan that skipped the check reported a clean
            // Create/Update and then silently wrote nothing — an import claiming success for a
            // schedule that never existed (reproduced on dev 2026-07-28).
            var cronTz = ctx.ScheduleService.ValidateCronAndTimezone(request.CronExpression, request.Timezone);
            if (cronTz.IsFailure)
            {
                items.Add(ErrorItem(ConfigSections.Schedules, marketId, name, cronTz.Error));
                continue;
            }

            // Same rule as the interactive PUT: a blank Name or Station is an attempt to clear a
            // required field, and both are length- and control-character-checked. Shared validator,
            // so the planner and the API can never disagree.
            var fields = ctx.ScheduleService.ValidateEditableFields(request.Name, request.Station);
            if (fields.IsFailure)
            {
                items.Add(ErrorItem(ConfigSections.Schedules, marketId, name, fields.Error));
                continue;
            }

            if (!existingByMarket.TryGetValue(marketId, out var existing))
            {
                if (!createCommand.HasValue)
                {
                    items.Add(ErrorItem(ConfigSections.Schedules, marketId, name,
                        "New market: Name, Station, Timezone, CronExpression, Coverage and " +
                        "TargetDurationSeconds are all required."));
                    continue;
                }

                // Domain field rules without saving; cron/timezone already cleared above.
                var validated = ctx.ScheduleService.CreateSchedule(createCommand.Value);
                if (validated.IsFailure)
                {
                    items.Add(ErrorItem(ConfigSections.Schedules, marketId, name, validated.Error));
                    continue;
                }

                items.Add(new PlannedConfigItem(
                    new ConfigImportItemDto(ConfigSections.Schedules, marketId, name, ConfigImportActions.Create),
                    ct => AsResult(ctx.ScheduleService.CreateOrUpdateAsync(updateCommand, createCommand, ct))));
                continue;
            }

            var updated = ctx.ScheduleService.UpdateSchedule(existing, updateCommand);
            if (updated.IsFailure)
            {
                items.Add(ErrorItem(ConfigSections.Schedules, marketId, name, updated.Error));
                continue;
            }
            // Compare CANONICALIZED coverage on both sides: Latitude/Longitude/RadiusKm are
            // derived-only for bbox/corridor/polygon shapes, so a persisted row whose derived
            // fields drifted from the current formula (older import, hand-edited storage row,
            // a future formula tweak) must not read as a real change when the actual shape
            // (the points/corners an operator drew) is untouched — see CoverageArea.Canonicalize.
            var existingDto = existing.ToConfigScheduleDto() with
            {
                Coverage = CoverageMapping.ToDto(existing.Coverage.Canonicalize())
            };
            var updatedDto = updated.Value.ToConfigScheduleDto() with
            {
                Coverage = CoverageMapping.ToDto(updated.Value.Coverage.Canonicalize())
            };

            if (JsonEquals(existingDto, updatedDto))
            {
                items.Add(UnchangedItem(ConfigSections.Schedules, marketId, name));
                continue;
            }

            items.Add(new PlannedConfigItem(
                new ConfigImportItemDto(ConfigSections.Schedules, marketId, name, ConfigImportActions.Update),
                ct => AsResult(ctx.ScheduleService.CreateOrUpdateAsync(
                    updateCommand, Option<CreateScheduleCommand>.None, ct))));
        }

        return Result.Success();

        static string? ValidateReferences(
            UpsertScheduleRequest request,
            HashSet<string> voiceProfileIds, HashSet<string> deliveryStyleIds, HashSet<string> playbookIds,
            HashSet<string> dictionaryIds, HashSet<string> globalDictionaryIds, HashSet<string> bookendIds)
        {
            if (!string.IsNullOrWhiteSpace(request.VoiceProfileId) && !voiceProfileIds.Contains(request.VoiceProfileId))
                return $"Voice profile '{request.VoiceProfileId}' does not exist.";
            if (!string.IsNullOrWhiteSpace(request.DeliveryStyleId) && !deliveryStyleIds.Contains(request.DeliveryStyleId))
                return $"Delivery style '{request.DeliveryStyleId}' does not exist.";
            if (!string.IsNullOrWhiteSpace(request.PlaybookId) && !playbookIds.Contains(request.PlaybookId))
                return $"Bulletin playbook '{request.PlaybookId}' does not exist.";
            if (!string.IsNullOrWhiteSpace(request.PronunciationDictionaryId)
                && !dictionaryIds.Contains(request.PronunciationDictionaryId))
                return $"Pronunciation dictionary '{request.PronunciationDictionaryId}' does not exist.";
            if (!string.IsNullOrWhiteSpace(request.PronunciationDictionaryId)
                && globalDictionaryIds.Contains(request.PronunciationDictionaryId))
                return $"Pronunciation dictionary '{request.PronunciationDictionaryId}' is platform-wide, so it " +
                       "already applies to every station and cannot be assigned to one.";
            // One bookend library: the assigned pair must be a known id in that library's
            // universe (rotation members are checked separately, so a member that does not
            // exist is reported as a rotation error rather than a primary error).
            if (!string.IsNullOrWhiteSpace(request.BookendId) && !bookendIds.Contains(request.BookendId))
                return $"Bookend '{request.BookendId}' does not exist.";
            return null;
        }
    }
}

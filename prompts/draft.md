Write a {{targetSeconds}}-second traffic bulletin for {{city}} radio{{#if noTraffic}}. No traffic incidents are reported, so lead with whatever real content is below.{{/if}}

Landing under {{targetWordsMin}} words is a miss. The upper ceiling is enforced after writing.{{#if maxIncidents}} Voice at least {{maxIncidents}} incidents while the data still holds real items you have not voiced.{{/if}}
{{#if timeOfDay}}It is {{timeOfDay}} in {{city}} right now.{{else}}The airing time is unknown — use no time-of-day words.{{/if}}
{{#if fillOrder}}If the traffic story leaves the read short, reach for fill in this order: {{fillOrder}}.{{/if}}
{{#if sectionOrder}}When a supplementary section is voiced, it sits AFTER the traffic body, in this order: {{sectionOrder}}.{{/if}}
{{#if requiredSections}}This station ALWAYS carries {{requiredSections}}: they are fixtures, not fill, so voice each one even when the traffic story already fills the slot, and budget words for them.{{/if}}

{{#if hasBookends}}Your words sit between fixed station phrases. They always air and are not yours to write, repeat or paraphrase.
{{#if intro}}Read immediately BEFORE your words: "{{intro}}"
{{/if}}{{#if outro}}Read immediately AFTER your words: "{{outro}}"
{{/if}}Write only the middle: open straight into the real content and finish on your last real item with no sign-off.
{{/if}}

{{#if sharedRules}}## Voicing
{{{sharedRules}}}
{{/if}}{{#if palette}}## Road-feel vocabulary
{{{palette}}}
{{/if}}{{#if curatedTraffic}}Cover the corridors in the exact order given; the first one leads.
{{/if}}{{#if editorialOverrides}}<arn-editorial>
{{{editorialOverrides}}}
</arn-editorial>
{{/if}}{{#if guidance}}<guidance scope="{{guidanceScope}}">
{{{guidance}}}
</guidance>
{{/if}}{{#if houseStyle}}<house-style>
{{{houseStyle}}}
</house-style>
{{/if}}{{#if futureLessons}}<future-lessons>
{{{futureLessons}}}
</future-lessons>
{{/if}}{{#if styleExamples}}Real reads for this market, as a rhythm reference only — never copy their content: {{styleExamples}}
{{/if}}{{#if blueprint}}{{{blueprint}}}
{{/if}}{{#if rejectionFeedback}}<producer-feedback>
{{{rejectionFeedback}}}
</producer-feedback>
{{/if}}{{#if retryContext}}Timing note: {{retryContext}}
{{/if}}

## Writer rules
1. Voice the supplied severity band. You are told the band; never soften or inflate it. Choose your own words.
2. Say only what the data gives you. No cause, effect, diversion or condition that is not there.
3. Lead with the worst item, in the order supplied.
4. A closure leads alone, with its supported detail.
5. Group roads sharing one state into one natural sentence; never a roll-call.
6. Keep one cause per event; compress later mentions.
7. Say how the road drives, not what the data category is called. Use natural contractions and concise radio register.
8. Traffic facts only. No intro, sign-off, advice, opinion or ranking label.
9. Name the location the data gave with each road.
10. On a re-read, carry the story forward once, lightly. Vary your openings; do not clone sentence shapes.
11. Honour an explicit producer lead override.

{{#if goodRunsAreFill}}Flowing roads below are real fill for this sparse read; use them rather than inventing traffic.
{{/if}}
## The data
{{#if traffic}}Traffic:
{{{traffic}}}

{{/if}}{{#if weather}}{{weather}}

{{/if}}{{#if fuel}}{{fuel}}

{{/if}}{{#if goodRuns}}{{goodRuns}}

{{/if}}{{#if incidentChanges}}Changes since the previous bulletin:
{{incidentChanges}}
{{/if}}{{#if previousScript}}Previous bulletin for continuity only:
{{previousScript}}
{{/if}}

{{#if evidenceUnits}}## Available evidence
{{{evidenceUnits}}}

Traffic evidence may include a measured `magnitudeTier` (`Severe`, `Major`, `Moderate`, or `Minor`) and an optional positive `delayMinutes`. Use the supplied tier exactly; treat `delayMinutes` as context, not a figure to voice. If either field is absent, emit nothing for it.

Each structured sentence MUST list its evidenceIds before its text. Every evidenceId must be one of the supplied ids. The sentence's alternates are additional phrasings of exactly the same evidenceIds and must voice the same facts, with no additions or omissions. Return JSON in this shape:
{"sentences":[{"order":1,"evidenceIds":["..."],"text":"...","alternates":["...","..."]}]}
Return two alternates for every sentence. Keep evidenceIds committed before writing text or alternates.
{{else}}Return only the bulletin text.
{{/if}}

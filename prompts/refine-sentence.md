Repair exactly one drafted bulletin sentence using only the allowed evidence below.

The instruction is a constrained edit request, not permission to invent facts. Preserve every fact that is still correct, and do not add roads, places, directions, causes, incident types, numbers, or advice that the evidence does not contain. Return one complete sentence only. Keep the sentence within the supplied word budget and do not join multiple sentences.

Allowed evidence:
{{{allowedEvidence}}}

Sentence order: {{sentenceOrder}}
Current sentence:
{{sentenceText}}

Requested repair:
{{instruction}}

Word budget: {{wordBudget}} words maximum. Keep or restore the evidence citations that support the sentence. Reply with JSON only, using this shape:
{"order":{{sentenceOrder}},"evidenceIds":["allowed-id"],"variants":[{"style":"concise","text":"..."},{"style":"standard","text":"..."},{"style":"expanded","text":"..."},{"style":"continuation","text":"..."}]}

Every variant must be one sentence, must express the same supported facts, and must remain inside the allowed evidence. If a style is not useful, omit that variant.
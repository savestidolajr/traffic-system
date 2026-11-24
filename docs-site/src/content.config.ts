import { defineCollection } from 'astro:content'
import { docsLoader } from '@astrojs/starlight/loaders'
import { docsSchema } from '@astrojs/starlight/schema'

// The sync script emits Starlight-native frontmatter only: provenance fields
// from the source docs (status, last_verified, source_of_truth) are rendered
// into the page body instead, so ARN can see them rather than having them
// hidden in metadata. No schema extension is needed.
export const collections = {
    docs: defineCollection({ loader: docsLoader(), schema: docsSchema() }),
}

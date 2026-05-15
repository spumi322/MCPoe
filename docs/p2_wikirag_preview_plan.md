# MCPoe P2 Wiki RAG - Preview Plan

Date: 2026-05-13

## Current State

P-1, P0, P1a, and P1b are complete enough to move on. P1c was skipped.

P2 is now Wiki RAG, not PoB-full. The old `mcpoe-plan.md` phase ordering is outdated.

The chunk-ready corpus lives in:

```text
G:\Code\wikiscraper\dataset
```

The reference RAG app is:

```text
G:\Code\dotRAG
```

Important correction made today:

- `clean_md` was already correct.
- The bad artifact was the review/handoff manifest.
- `dataset/rag_scope_policy.jsonl` was changed so `general/article` is included.
- `dotnet run -- --review-clean-dataset` was rerun in `G:\Code\wikiscraper`.
- `dataset/chunk_sources.jsonl` now has `11465` eligible pages and `0` scope-excluded pages.

Current review stats:

```text
reviewed: 11470
eligible_for_chunking: 11465
rejected: 5
scope_excluded: 0
core: 9473
support: 1992
```

Spot-checked included pages:

- Burning
- Ignite
- Damage over time
- Loreweave
- Orb of Alchemy

## Architecture Decision

Use `wikiscraper` as the offline corpus indexer/chunker.

Use `MCPoe` only as the runtime MCP server that consumes a local indexed artifact.

This keeps concerns clean:

```text
wikiscraper:
wiki API -> raw wikitext -> clean_md -> chunk_sources.jsonl -> chunks -> document embeddings -> vectors.db

MCPoe:
Claude -> search_wiki -> Voyage query embedding -> local vectors.db retrieval -> sourced chunks
```

MCPoe should not scrape, clean, classify, chunk, or embed the full document corpus during startup.

## Artifact Contract

Target output from `wikiscraper`:

```text
G:\Code\wikiscraper\dataset\vectors.db
G:\Code\wikiscraper\dataset\wiki_chunks.jsonl
G:\Code\wikiscraper\dataset\index_stats.json
G:\Code\wikiscraper\dataset\index_manifest.json
```

`wiki_chunks.jsonl` is useful for debugging and review. `vectors.db` is the runtime artifact MCPoe consumes.

MCPoe config should point to the produced DB:

```json
{
  "WikiRag": {
    "VectorsPath": "G:/Code/wikiscraper/dataset/vectors.db",
    "EmbeddingModel": "voyage-4-lite",
    "TopK": 5,
    "MinScore": 0.5
  }
}
```

## wikiscraper Work

Add offline indexing stages:

```powershell
dotnet run -- --chunk-dataset
dotnet run -- --embed-chunks
dotnet run -- --build-vector-db
```

A combined command can come later:

```powershell
dotnet run -- --index-rag
```

### Chunk Dataset

Input:

```text
dataset/chunk_sources.jsonl
dataset/clean_md/**
dataset/aliases.jsonl
```

Output:

```text
dataset/wiki_chunks.jsonl
dataset/chunk_stats.json
```

Chunking rules:

- Split by markdown headings first.
- Preserve section breadcrumbs.
- Split oversized sections by paragraph.
- Use roughly `1800-2400` chars per chunk.
- Use overlap only for split oversized sections.
- Include title/theme/page type/class/rag tier in `embedding_text`.
- For `retrieval_hint: metadata_context_required`, prepend `embedding_context` before content.
- Treat the opening key/value metadata block as a compact data chunk, especially for item and skill pages.

Chunk row shape:

```json
{
  "chunk_id": "stable-hash-or-pageid-index",
  "page_id": 13821,
  "title": "Righteous Fire",
  "theme": "skills",
  "page_type": "skill_gem",
  "class_id": "Active Skill Gem",
  "rag_tier": "core",
  "clean_path": "dataset/clean_md/skills/13821_Righteous Fire.md",
  "chunk_index": 0,
  "section": "Skill functions and interactions",
  "content": "...",
  "embedding_text": "Title: ...\nSection: ...\n\n...",
  "content_hash": "...",
  "embedding_text_hash": "..."
}
```

### Embed Chunks

Use VoyageAI, likely `voyage-4-lite` first for free-tier friendliness.

Requirements:

- API key from environment, e.g. `VOYAGE_API_KEY`.
- Batch embedding.
- Configurable batch size, default conservative.
- Backoff and resume on `429`.
- Skip unchanged chunks by `embedding_text_hash + model`.
- Persist after each batch.
- Never require re-embedding the full corpus when only a few chunks changed.

### Build Vector DB

Initial schema:

```text
wiki_pages
wiki_chunks
wiki_embeddings
wiki_aliases
wiki_chunks_fts
index_metadata
```

Store embeddings as float32 blobs first. Add `sqlite-vec` later after the basic pipeline works. Brute-force cosine over local embeddings is acceptable for the first P2 validation.

## MCPoe Work

Replace the `search_wiki` stub with a real read-only runtime service.

Runtime responsibilities:

- Load config for `WikiRag:VectorsPath`.
- Open `vectors.db` read-only.
- Embed only the user query with VoyageAI.
- Retrieve matching chunks from local DB.
- Return compact source-grounded chunks to the MCP client.

Tool shape:

```csharp
search_wiki(string query, int top_k = 5, string? theme = null)
```

Runtime retrieval order:

1. Query embedding via VoyageAI with `input_type = "query"`.
2. Vector search top 30-50.
3. FTS search top 30-50.
4. Merge and rerank:
   - vector similarity
   - exact/near title match boost
   - FTS/title match boost
   - `core` tier boost over `support`
5. Return top `top_k`.

Do not synthesize answers in the tool. Return sources and chunks; let Claude answer.

## Useful Code From dotRAG

Reuse ideas, not necessarily files directly:

- `MarkdownChunker`
- `VoyageEmbeddingService`
- embedding cache/hash strategy
- cosine scoring
- retrieval logging
- chunker tests

Avoid copying:

- in-memory-only vector store as the production path
- startup ingestion pattern
- single JSON embedding cache for this larger corpus

## Tomorrow's First Tasks

1. In `wikiscraper`, add `--chunk-dataset`.
2. Port/adapt the dotRAG markdown chunker.
3. Emit `dataset/wiki_chunks.jsonl`.
4. Produce chunk stats:
   - pages read
   - chunks emitted
   - chunks by theme/page type/rag tier
   - largest chunks
   - empty/tiny chunks
5. Inspect sample chunks before spending Voyage tokens.

Validation pages:

- Righteous Fire
- Burning
- Ignite
- Damage over time
- Headhunter
- Orb of Alchemy

## First Retrieval Test Prompts

Use after indexing:

```text
What does Righteous Fire do and how do you sustain it?
Can Righteous Fire ignite?
How does burning damage stack?
Does damage over time hit?
What is the difference between ignite and burning?
How does Headhunter work?
What does Orb of Alchemy do?
```

## Cautions

- MCPoe uses stdio transport. Do not add console output to MCPoe runtime code.
- wikiscraper can print progress freely.
- Do not reintroduce subtheme-driven directory structure.
- Do not regenerate `clean_md` unless the cleaner itself changes.
- Do not embed until chunk output is reviewed.
- Keep the indexed artifact schema versioned so MCPoe can fail clearly if the DB is stale or incompatible.

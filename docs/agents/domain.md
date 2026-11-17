# Domain Docs

How the engineering skills should consume this repo's domain documentation when exploring the codebase.
Scribble is a single-context repo: one glossary, one decision log.

## Before exploring, read these

- **`GLOSSARY.md`** at the repo root: the words to use (view, element, element type, settings type, property,
  setting, value, theme value, app value, visual state, layer, host, ...).
- **`docs/DESIGN.md`**: the decision log. Read the sections that touch the area you're about to work in
  (the "Decisions for Mx" sections, the dated topic sections, and "Milestone summaries").

If either file is missing, **proceed silently**. Don't flag its absence; don't suggest creating it upfront.

## No ADR files

Don't create `docs/adr/` or ADR files (Tim, 2026-11-16). A decision that settles goes into `docs/DESIGN.md`,
with its date and who decided it; a term that settles goes into `GLOSSARY.md`. Skills that would write an ADR
(`/domain-modeling`, `/grill-with-docs`, `/improve-codebase-architecture`) write a `docs/DESIGN.md` section instead.

## File structure

```
/
├── GLOSSARY.md
├── CLAUDE.md          ← the working summary
├── docs/
│   ├── DESIGN.md      ← every decision, dated
│   └── CHECKLIST.md   ← what Tim should try next
└── src/
```

## Use the glossary's vocabulary

When your output names a domain concept (in an issue title, a refactor proposal, a hypothesis, a test name), use
the term as defined in `GLOSSARY.md`. Don't drift to synonyms the glossary explicitly avoids.

If the concept you need isn't in the glossary yet, that's a signal: either you're inventing language the project
doesn't use (reconsider) or there's a real gap (add the term once it settles).

## Flag conflicts with recorded decisions

If your output contradicts a decision in `docs/DESIGN.md`, surface it explicitly rather than silently overriding,
naming the section:

> _Contradicts DESIGN.md "Decisions for M8", decision 16, but worth reopening because…_

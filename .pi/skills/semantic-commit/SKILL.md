---
name: semantic-commit
description: >
  Create perfect git commits following the Conventional Commits / semantic commit
  standard. Analyzes staged and unstaged changes, proposes how to split them into
  logical atomic units, drafts well-formed commit messages for each, and executes
  after user approval. Use this skill whenever the user wants to commit, says
  "commit my changes", "let's commit", "create a commit", "write a commit message",
  or asks how to structure commits. Also trigger when the user mentions semantic
  commits, conventional commits, commit types like feat/fix/chore, or wants help
  splitting changes into separate commits.
---

# Semantic Commit Skill

The goal of a good commit history is to tell a clear story: what changed, why it
changed, and what kind of change it was. Conventional Commits give every reader
(human or tool) an instant signal about intent — a `feat` adds capability, a `fix`
corrects something broken, a `refactor` restructures without changing behavior.
Getting this right pays off in changelogs, release automation, and code review.

---

## Critical: Never use `git add .` or `git add -A`

Stage specific files or hunks only. Blanket staging silently includes secrets,
build artifacts, lock files, or changes from a different task. Always:

```bash
git add src/specific/file.ts          # one file
git add src/auth.ts src/types.ts      # multiple specific files
git add -p src/api/fixtures.ts        # specific hunks within a file
```

---

## Commit Message Format

```
<type>[optional scope]: <short description>

[optional body — explain WHY, not just what]

[optional footer(s): BREAKING CHANGE, Closes #123, etc.]
```

**Subject line rules:**
- 50 characters or fewer (hard cap: 72)
- Imperative mood: "add login page" not "added" or "adds"
- No trailing period
- Lowercase after the colon

**Body** — use it when the motivation isn't obvious from the subject. Wrap at 72
characters. Explain what changed and why, not just what the diff shows.

---

## Core Types

The four types you'll use 95% of the time:

| Type | When to use | SemVer |
|------|-------------|--------|
| `feat` | New feature or behavior visible to users/callers | MINOR |
| `fix` | Bug fix visible to users/callers | PATCH |
| `refactor` | Internal restructure — no new behavior, no bug fix | none |
| `docs` | Documentation only (README, comments, JSDoc) | none |

**Other types** (use when clearly appropriate):
- `test` — adding or correcting tests
- `style` — whitespace/formatting, no logic change
- `perf` — performance improvement, same behavior
- `build` / `ci` — build system, dependencies, CI pipeline
- `chore` — maintenance tasks not affecting src or tests

**Scope** (optional): the subsystem affected, in parentheses — `feat(auth)`,
`fix(api)`. Use it when the change is clearly confined to one module. Omit it
when the change is broad or cross-cutting.

**Breaking changes**: append `!` after type/scope (`feat!:`, `refactor(api)!:`)
and add a `BREAKING CHANGE: <description>` footer explaining what breaks and how
to migrate. Both markers are required for changelog tooling.

---

## Splitting Changes into Logical Units

A commit should capture one coherent intent. Ask: "Can I describe this in one
sentence without 'and'?" If not, split it.

**Signals to split:**
- Different subsystems changed for unrelated reasons
- Mix of `refactor` and `feat` — they have different SemVer meaning and different
  reasons to revert
- Whitespace/formatting mixed with logic changes
- Dependency bump mixed with feature code

**When the same file has changes for two commits**, use `git add -p`:

```bash
git add -p src/api/fixtures.ts
```

Interactive keys:
- `y` — stage this hunk
- `n` — skip this hunk (stage later)
- `s` — split the hunk into smaller pieces
- `q` — quit (remaining hunks stay unstaged)

---

## Commit Ordering

When commits build on each other, the foundation goes first:
- Refactor before feature (feature lands on clean code)
- Test infrastructure before tests
- Migration before code that uses it

This way every commit in the log is in a valid, runnable state.

---

## Workflow

### Step 1: Understand the changes

```bash
git status          # see what's staged vs unstaged
git diff            # unstaged changes
git diff --staged   # staged changes
```

Read the diff carefully. Understand what each file change is doing and why.

### Step 2: Plan the split

Group files and hunks by intent. For each proposed commit:
- Which files / hunks belong together?
- What type and optional scope fits?
- Draft a subject line (keep it under 50 chars)

### Step 3: Present the plan

Show the user a clear proposal before touching git:

```
Here's how I'd split this:

1. refactor(api): extract pagination helper into shared util
   → src/api/utils.ts (new), src/api/fixtures.ts (hunk 1 via git add -p)

2. feat(fixtures): add season filter to fixture list endpoint
   → src/api/fixtures.ts (hunk 2), src/types/fixture.ts

Ready to execute — want me to run these, or adjust the split first?
```

Always end with that closing question. The user may want to rename a scope,
merge commits, or adjust which files go where.

### Step 4: Execute on confirmation

For each commit in order:

```bash
git add <specific files>           # or: git add -p <file> for mixed hunks
git commit -m "$(cat <<'EOF'
type(scope): short description

Optional body explaining the why.

Optional footer: Closes #42
EOF
)"
```

After all commits:

```bash
git log --oneline -n <number of commits>
```

### Step 5: Attribution

Do **not** add "Co-Authored-By: Claude" or "Generated with Claude Code". Commits
should read as purely user-authored.

---

## Examples

**Too vague:**
```
fix: bug fix
feat: new stuff
```

**Wrong mood / too long:**
```
feat(auth): Added JWT-based authentication system with refresh tokens and rate limiting
```

**Just right — simple change, no body needed:**
```
docs: fix typo in README (authentification → authentication)
```

**Just right — complex change with body:**
```
feat(auth): add JWT authentication with refresh token support

Replaces session-based auth. Tokens expire in 15 min; refresh tokens
last 7 days and are rotated on use. Rate limiting (5 req/min) added
to /auth/login to prevent brute force.

Closes #88
```

**Breaking change:**
```
refactor!: rename getFixtures to fetchFixtures throughout codebase

Aligns with the fetch* naming convention used by other async helpers.

BREAKING CHANGE: getFixtures() renamed to fetchFixtures(). Update all
call sites. Function signature and return value are unchanged.
```

---

## Common Judgment Calls

- **Dependency bump + new API usage**: one `feat` commit is fine — the bump serves the feature.
- **Linting fixes discovered while working**: bundle as `style:` in a separate commit, or prefix the main commit subject with the type that matters most.
- **Rename with no behavior change**: `refactor`, not `feat` — the API surface changed but the semantics didn't.
- **Optional new param (backward-compatible)**: `feat`, no `!` needed.
- **Tiny one-liner fix**: no body required; the subject is enough.

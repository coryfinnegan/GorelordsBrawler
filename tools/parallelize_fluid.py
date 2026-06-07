#!/usr/bin/env python3
"""
One-shot source transform: wrap the JACOBI-SAFE per-particle loops in
FluidSimulation.cs with a range-partitioned Parallel.ForEach helper.

Why a script (not hand edits): the Read/Edit tooling mis-renders this file's
tabs, making byte-exact multi-line edits unreliable. Operating on real bytes in
Python with brace-matching + balance assertions is safe and verifiable; the C#
compiler and the existing Fluid.Tests correctness suite are the final proof.

WHICH loops are safe to parallelize (verified by reading the solver):
  Predict                      writes PredX/PredY[i]            — safe
  ComputeLambda                writes _lambda[i]                — safe
  ComputeAndApplyDeltaP (x2)   writes _dpx/_dpy[i]; PredX/Y[i]  — safe
                               (calls FluidCollider.Project — read-only on
                                shared _aabbs[], writes only ref params)
  UpdateVelocitiesAndPositions writes Px/Py/Vx/Vy[i]            — safe
                               (calls ContactSide — read-only)
  XsphViscosity (x3)           clear/accumulate/apply _dpx/_dpy/Vx/Vy[i] — safe

EXPLICITLY EXCLUDED (would race or break):
  BuildSpatialHash (x2)  writes SHARED _cellStart[c+1]++ and _cellIdx[slot]
                         — scatter/count, classic write-write race. Left serial.
  DespawnOffscreen       reverse loop, mutates Count via Despawn(). Not matched
                         by our header pattern anyway.

So we transform loops in a per-method allow-list, NOT every match. Each wrapped
loop writes only its own index i (Jacobi-style PBF) — partitioning [0,Count)
across threads has no write-write races (SPH/PBD parallelization literature).

Idempotent: refuses to run if RunPerParticle already present.
"""
import re, sys

F = 'GorelordsBrawler/Components/Hazards/Fluid/FluidSimulation.cs'
s = open(F, encoding='utf-8').read()

if 'RunPerParticle' in s:
    print('already transformed — nothing to do')
    sys.exit(0)

HEADER = '\t\t\tfor (int i = 0; i < Count; i++)'

# Methods whose per-particle loops are Jacobi-safe, with how many such loops
# each contains (asserted, so a refactor that changes the count fails loudly).
ALLOW = {
    'Predict': 1,
    'ComputeLambda': 1,
    'ComputeAndApplyDeltaP': 2,
    'UpdateVelocitiesAndPositions': 1,
    'XsphViscosity': 3,
}
# Methods we must NOT touch even though they contain the same header pattern.
# BuildSpatialHash: shared-scatter race. ApplyImpulseInRadius: public, called on
# the main thread for splash/surge effects (outside Step) — not a solver stage.
DENY = {'BuildSpatialHash', 'ApplyImpulseInRadius'}

# Map byte offset -> enclosing method name. Match BOTH private and public methods
# so the offset->name resolution can't drift past a public method (which would
# mislabel a later loop). Any modifier, any return type.
method_spans = [(m.start(), m.group(1))
                for m in re.finditer(r'(?:public|private|internal|protected)\s+(?:static\s+)?(?:void|float|int|bool|Vector2)\s+(\w+)\s*\(', s)]
def method_of(pos):
    name = '?'
    for st, nm in method_spans:
        if st <= pos:
            name = nm
        else:
            break
    return name

# Find every candidate loop, classify by method.
sites = []          # (h, close, method)
counts = {}
idx = 0
while True:
    h = s.find(HEADER, idx)
    if h < 0:
        break
    meth = method_of(h)
    # match the loop's brace block
    brace = s.index('{', h + len(HEADER))
    depth, k = 0, brace
    while k < len(s):
        if s[k] == '{':
            depth += 1
        elif s[k] == '}':
            depth -= 1
            if depth == 0:
                break
        k += 1
    assert depth == 0, f'unbalanced braces in {meth}'
    counts[meth] = counts.get(meth, 0) + 1
    if meth in ALLOW:
        sites.append((h, k, meth))
    elif meth in DENY:
        print(f'  skipping loop in {meth} (deny-list)')
    else:
        raise SystemExit(f'ABORT: loop in unrecognised method {meth!r} at {h} — classify it first')
    idx = k + 1

# Verify we saw exactly the loop counts we expect per allowed method.
for meth, want in ALLOW.items():
    got = counts.get(meth, 0)
    assert got == want, f'{meth}: expected {want} safe loops, found {got}'
assert counts.get('BuildSpatialHash', 0) == 2, 'expected 2 BuildSpatialHash loops to skip'
print(f'wrapping {len(sites)} loops:', ', '.join(f'{m}' for _, _, m in sites))

# Transform, last->first so byte offsets stay valid. For each loop we:
#   1. find the original body (between the loop's own '{' and matching '}')
#   2. re-indent that body +1 tab (it's now one level deeper: lambda > for > body)
#   3. rebuild as: RunPerParticle((__lo,__hi) => { for(...) { <body> } });
# This preserves the tab convention and yields a minimal, reviewable diff
# (only the wrapped regions change, not the whole file).
for h, close, _ in reversed(sites):
    body_open = s.index('{', h + len(HEADER))     # the loop's own opening brace
    body = s[body_open + 1:close]                  # everything between { and }
    # +1 tab on every non-empty line of the body
    reindented = '\n'.join(
        ('\t' + ln if ln.strip() else ln) for ln in body.split('\n'))
    block = (
        '\t\t\tRunPerParticle((__lo, __hi) =>\n'
        '\t\t\t{\n'
        '\t\t\t\tfor (int i = __lo; i < __hi; i++)\n'
        '\t\t\t\t{' + reindented + '\t\t\t\t}\n'
        '\t\t\t});')
    s = s[:h] + block + s[close + 1:]

# usings
anc = 'using Microsoft.Xna.Framework;\n'
assert s.count(anc) == 1
s = s.replace(anc, anc + 'using System.Collections.Concurrent;\nusing System.Threading.Tasks;\n')

# fields before ctor
fields = '''\t\t// ── Parallelism ───────────────────────────────────────────────────────
\t\t/// <summary>
\t\t/// When true (default) the Jacobi-style per-particle solver stages run
\t\t/// multi-threaded via range-partitioned Parallel.ForEach. Set false to force
\t\t/// the serial path (the benchmark toggles this to measure speedup; also an
\t\t/// escape hatch). Safe because every parallel stage READS neighbour state and
\t\t/// WRITES only its own index (_lambda[i]/_dpx[i]/PredX[i]/Vx[i]) — no
\t\t/// write-write races. BuildSpatialHash (shared scatter) stays serial.
\t\t/// </summary>
\t\tpublic bool ParallelEnabled = true;

\t\t// Below this particle count fork/join overhead outweighs the win → run
\t\t// serial. Calibrated empirically (see FluidBenchmark).
\t\tprivate const int ParallelThreshold = 512;

'''
ctor_anchor = '\t\tpublic FluidSimulation(\n'
assert s.count(ctor_anchor) == 1
s = s.replace(ctor_anchor, fields + ctor_anchor)

# helper before Step
helper = '''\t\t// Run a per-particle stage over [0, Count) serially or across all cores.
\t\t// Range partitioning invokes the delegate once per CHUNK, not per particle —
\t\t// essential for tight numeric loops where per-element delegate overhead would
\t\t// dominate (.NET TPL guidance: "How to: Speed Up Small Loop Bodies").
\t\tprivate void RunPerParticle(System.Action<int, int> rangeBody)
\t\t{
\t\t\tint count = Count;
\t\t\tif (!ParallelEnabled || count < ParallelThreshold)
\t\t\t{
\t\t\t\trangeBody(0, count);
\t\t\t\treturn;
\t\t\t}
\t\t\tParallel.ForEach(Partitioner.Create(0, count),
\t\t\t\trange => rangeBody(range.Item1, range.Item2));
\t\t}

'''
step_anchor = '\t\tpublic void Step(float dt, FluidCollider colliders)\n'
assert s.count(step_anchor) == 1
s = s.replace(step_anchor, helper + step_anchor)

assert s.count('{') == s.count('}'), 'brace imbalance after transform!'
assert s.count('RunPerParticle((__lo, __hi) =>') == len(sites)

open(F, 'w', encoding='utf-8', newline='\n').write(s)
print(f'transform OK: {len(sites)} loops wrapped, BuildSpatialHash left serial, braces balanced')

# Prompt library

The agents in [../agents/](../agents/) define **who** does a stage: role, tools, inputs, steps and limits. The prompts here define **how** the work is done: the reasoning technique, a self-check and the exact output format. Each agent reads its prompt before it starts. That keeps the agent files short and lets a prompt be improved without touching tool permissions.

| Prompt | Agent | Technique |
|---|---|---|
| [plan.md](plan.md) | `planner` | Requirement decomposition: restate the goal, extract or derive numbered acceptance criteria, ground every change in real code, choose the smallest design. Fixed Markdown output that the implementer consumes, with criterion → test mapping |
| [implement-csharp.md](implement-csharp.md) | `implementer` | Layer-by-layer generation (DTO → Data → API): read neighbouring code and copy its patterns, test per acceptance criterion, build, format only changed files, one building commit per layer |
| [repair-diagnostics.md](repair-diagnostics.md) | `qa` | Diagnostic reflection: a written diagnosis (failure, location, kind, cause, owner, fix) before every edit, a table of common `CSxxxx` causes, hard limits against weakening tests, and a stop rule for fixes that do not converge |
| [pr-body.md](pr-body.md) | `release` | Reviewer-oriented PR: behaviour over file lists, numbers only from the run log, explicit deviations, fixed template ending with `Closes #<n>` |

Techniques shared by all prompts:
- **explicit inputs and output schema**, so that a stage can be parsed by the next one;
- **a self-check list** that runs before the agent returns;
- **negative constraints**: what never to do;
- **one worked example** where the format is easy to get wrong.

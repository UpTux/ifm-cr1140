# Triage labels

This file maps the five canonical triage roles to the label strings this repo actually uses.

The `triage` skill reads this file to know which labels to apply. If you rename a label here, the skill uses the new name.

## Label mapping

| Canonical role | Label in this repo | Meaning |
|----------------|--------------------|---------|
| `needs-triage`     | `needs-triage`     | Maintainer needs to evaluate |
| `needs-info`       | `needs-info`       | Waiting on reporter |
| `ready-for-agent`  | `ready-for-agent`  | Fully specified, AFK-ready (an agent can pick it up with no human context) |
| `ready-for-human`  | `ready-for-human`  | Needs human implementation |
| `wontfix`          | `wontfix`          | Will not be actioned |

## Notes

- With a local-markdown issue tracker, these are the values the `status:` frontmatter field can take (plus `done` for closed issues).
- With GitHub/GitLab, these are label names that must exist in the tracker (create them if missing).
- If you add a new triage state, add a row here so the skill knows about it.

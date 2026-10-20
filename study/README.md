# Run the Solvia usability kit

No results are included. Human templates contain headers only. The assignment
file is a proposed schedule; it is not evidence of attendance or consent.
Current work uses **agent persona reviews**, separately from the future
6–8-adult protocol in [USABILITY-STUDY.md](../docs/USABILITY-STUDY.md).

1. Read the protocol and [task cards](task-cards.md).
2. For agent reviews, copy `agent-review.template.csv` outside the repository
   and fill only actually observed actions. Use `provenance=agent`.
3. For a future human study, obtain consent first, then copy
   `observations.template.csv` and `retrieval.template.csv` to private storage.
   Use `provenance=human`. Follow `assignment-plan.csv`; leave unobserved
   results blank. Do not publish participant notes or contacts by default.
4. The reporter needs Python 3.10+ and only its standard library. For a
   completed observation file, run from the repo root:

   ```powershell
   python tools/usability-summary.py C:/private/study-observations.csv
   ```

5. To inspect the deliberately fake validation fixture:

   ```powershell
   python tools/usability-summary.py study/synthetic-validation.csv --allow-synthetic
   ```

The output is descriptive, not an impact estimate or statistical test.
Blank input yields “No observed tasks.” Synthetic output is prominently
labeled and must not be used as participant evidence. Actual agent review
CSV uses a diagnostic schema, so summarize its concrete findings rather
than passing it to the paired human-timing reporter.

Field conventions: `condition` is `ai` or `classic`; `sequence` is 1 or 2;
`task_status` is `completed`, `timeout`, `abandoned` or `technical_failure`.
Use UTC observation time, the tested app commit, and `reader_source` of
`together`, `ollama`, `fallback` or `none`. Counts are whole numbers; durations
are nonnegative seconds. Correctness booleans are `true` or `false`, not a
guessed score. Preserve failures and missing values. `notes` must contain
no real plates or personal information.

Completion requires status completed, zero unresolved wrong fields and the
correct stated total. Payment/Sandbox understanding remains a separate
guardrail. The reporter shows completion counts and successful-task timing;
paired timing only includes people who succeeded in both paths and is
explicitly labeled with that limitation. It does not analyze PDF retrieval,
invent missing values, compute ROI or claim human savings.

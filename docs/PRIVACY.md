# Privacy — why our AI is built this way

Student data is private. That is not a slogan for us — it decided how this app was built.

This page explains, in plain words: what the California Community Colleges Chancellor's Office guidance and the California Department of Education model policy say, and how Solvia answers each point.

## 1. What the guidance says

Source: Chancellor's Office AI site — [ai.cccco.edu](https://ai.cccco.edu/) and its Guidance and Policy page — [ai.cccco.edu/guidance-and-policy](https://ai.cccco.edu/guidance-and-policy/) (Safety and Security FAQ, last updated 05/08/2026).

1. **Student records are protected by FERPA.** FERPA generally prohibits sharing a student's personally identifiable information (PII) with third parties without consent. That includes names paired with grades, attendance, or other school information.
2. **Do not let AI tools share or train on your data.** The guidance tells users to always select **"Do not share data"** and **"Do not allow model training"** (or the equivalent setting) in any AI tool.
3. **Check before student data goes into any AI tool.** Before entering student data into an AI tool, consult your institution's legal or compliance office — or use examples that do not reference real individuals.
4. **Vendor agreements are the protection layer.** Sharing student data is permissible in some cases when a vendor agreement is in place that meets FERPA's restrictions. Enterprise agreements protect more than free or personal accounts.

One more California source says the same thing in one line: the California Department of Education's Model Policy on AI in Education, §7, states that all AI tools should protect student data and **prohibit its use for training public AI models** or advertising. ([cde.ca.gov/ci/pl/aipolicy.asp](https://www.cde.ca.gov/ci/pl/aipolicy.asp))

In short, as both sources frame it: **student data under FERPA must never be used to train a model, and must never go to third parties without consent.**

## 2. How Solvia answers each point

Solvia's AI is **Microsoft Phi-4-mini** — a U.S. (Microsoft) open model, MIT license — running **100% locally on the student's own computer** via Ollama. It never runs in a cloud.

1. **FERPA:** student data is never shared with a third party by the AI at all — the model is on the student's own machine. (In this demo, Maria is fictional and all payments are PayPal Sandbox test money; no real student data is used.)
2. **No training, ever:** there is no setting to get wrong. Because the model runs locally, student data never reaches any model-training pipeline. Nothing can train on it. That is not a promise about a setting — training on the student's data is **impossible by design**, because the data never leaves the computer.
3. **No risky data entry:** Solvia never asks a student to paste records into an outside AI service. The risky step the guidance warns about does not exist in this design.
4. **No vendor agreement needed for the AI:** no student data ever reaches an AI vendor, so there is no AI vendor holding student data. PayPal Sandbox handles only the payment step — the money, not the student's information.

The CDE §7 line is answered the same way: training use is not just switched off — it is impossible in this design, because the data never leaves the computer.

Two more design facts carry the same privacy logic:

- **The AI can never set a price.** The extraction result type (`ExtractionResult` in `src/Solvia/Services/IAiExtractor.cs`) has **no price field by construction** — not hidden, not nullable, absent. The model reads a student's words into a permit draft (college, permit type, term, car), and nothing else.
- **Prices come from the campus's own published fees.** Solvia prices a permit only from its own fee catalog (`IFeeCatalog`, over `data/fees.csv` — the colleges' published fee rows), server-side. The student's words — and the model — never supply, alter, or invent a price.

The outbound payment-complete webhook follows the same rule (see `docs/zapier-recipe.md`): its event carries permit facts only — **no student name, no email, no plate**.

## 3. Our country rule

**No China tools, models, or services. No Russia tools, models, or services. Anywhere in the stack. China and Russia are out — only those two.**

That is why Qwen was evaluated and **not used**: Qwen is a China model. We chose a U.S. model instead — Microsoft Phi-4-mini — and we run it locally, so student privacy does not depend on any company's promise.

This is a rule about the AI tools, models, and services in this stack — stated exactly as our own choice, in the box below. It is not a claim about the origin of every library, host, or network this demo touches.

## 4. Honesty box — what we do NOT claim

> **Neither the California Community Colleges Chancellor's Office nor the State of California banned China AI. We have read their guidance page by page, and no such ban exists. We never claim that it does.**
>
> California's rules are about the *data*: protect it (FERPA), do not share it with third parties without consent, and never let it be used for training. The local, U.S. model in Solvia is **our own choice**, made on purpose, for student privacy. It satisfies the real guidance by design — the data never leaves the student's computer, so it can never be shared and can never be trained on.
>
> We also do **not** claim the whole stack is "USA only." The country rule above is exactly what it says — China and Russia are out, only those two — and the privacy protection in this design comes from the model running locally, not from a claim about every component's country.

## Sources

- Chancellor's Office AI hub: https://ai.cccco.edu/
- Chancellor's Office — Guidance and Policy (Safety and Security FAQ, updated 05/08/2026): https://ai.cccco.edu/guidance-and-policy/
- California Department of Education — Model Policy: Artificial Intelligence in Education (§7): https://www.cde.ca.gov/ci/pl/aipolicy.asp

# PayPal AI Hackathon — Solvia rules audit

Reviewed October 19, 2026. **Solvia is not ready for final submission yet.** The PayPal Sandbox matrix is verified, the public source is MIT-licensed, and the draft story plus factual judge instructions are saved. The principal gaps are live AI evidence, a public YouTube demo, clean full-path judge-access verification, remaining personal submission fields, and participant eligibility/rights confirmations.

This audit covers every section of the supplied official rules. It records evidence and unresolved obligations; it does not certify personal eligibility, ownership, legal clearance, acceptance of terms, or an award. Passing tests and saving a draft do not establish those facts.

## Sources and fidelity

- Primary audit text: the user's attachment `Pasted text.txt`, attachment ID `4065b67e-4ffd-4ac0-a666-2cbf53e30a8b`, **34,461 bytes / 480 lines**, read in full without omitting the prize table or final legal sections. SHA-256: `5A1323FF9C0A850AD69D3787A3783E4EFD1EF3D726E28D87950F6B78E121C2CA`.
- Live cross-check: [official rules, sections 1–15](https://paypalaihackathon.devpost.com/rules), checked the same day. The substantive requirements below are mapped from the supplied text; the live page's dates, criteria, complete prize eligibility table, and closing sections were also reviewed. No substantive discrepancy was identified; this is not a byte-for-byte HTML equivalence claim.
- Incorporated references: [Devpost Terms](https://info.devpost.com/legal/terms-of-service), updated December 5, 2025; [Devpost Privacy](https://info.devpost.com/legal/privacy-policy), effective August 10, 2026; linked California/GDPR notices where applicable.
- Product evidence: repository README, LICENSE, NOTICES, `docs/DEVPOST-STORY.md`, `docs/PRIVACY.md`, clean-install and test records, sponsor records, fee CSV, model adapter, Sandbox matrix and PDF validation reports. GitHub metadata independently confirmed `jlhpacheco/solvia` is **public**, and the remote `LICENSE` is MIT. Review baseline includes commit `b5f44c0` and ongoing working-tree hardening; final results must be refreshed after those edits.
- Devpost UI evidence: Solvia submission `1245990` in hackathon `31302`; story, tagline, tools, logo and three screenshots saved. A later save/read-back verified Additional Info fields **4, 5, 6, 7, 8, 9, 10, 14, 15 and 19**: New/N/A, technology, AG Grid, repository/setup links, testing instructions, sponsor experience and AI disclosure. The submission still remained **Draft, 2 of 5 steps complete**. Participant fields remain unanswered and eligibility checkboxes 16–18 were verified **unchecked**. These are actual saved-form observations, not assumptions from icons or HTML `required` attributes.

Status terms: **Verified** means the cited evidence supports that limited item. **Partial** means some evidence exists. **Missing** means the required artifact/action is absent. **Participant** needs facts or commitments from the entrant. **Ongoing** continues after the build. **Conditional** applies only if that situation/tool/prize is chosen. Organizer discretion remains where the rules reserve it.

## Immediate blockers and priorities

| Priority | Remaining action | Why it matters | Owner/status |
|---|---|---|---|
| P0 | Run the configured Phi-4-mini/Ollama path and retain a genuine `ollama` result, with editable user review | The app must integrate PayPal with AI, and implementation must be meaningful. A rules fallback and mocked AI responses do not demonstrate the actual model | Technical; missing runtime proof |
| P0 | Record, upload, and link a **public YouTube** video under three minutes showing the actual app working end to end | An explicit submission artifact; a video URL is still absent | Technical/participant; missing |
| P0 | Complete remaining Additional Info, eligibility facts, team details and final submission steps | Factual technical answers and New/N/A are saved; saving a draft is not entering a complete submission | Participant fields and final submission; incomplete |
| P0 | Confirm the judge access plan, then test it from a clean environment with real AI and Sandbox payment | A localhost page on the developer's computer is not remotely accessible to judges. The earlier clean-install proof covered the free/fallback path | Technical; partial |
| P0 | Confirm age, residence/domicile, exclusions, conflicts, representation, provenance and rights | These cannot be inferred from a profile, GitHub ownership, tests, or the assistant's opinion | Participant; unconfirmed |
| P1 | Align all public claims with the final build and exact test boundaries | No claims of live AI, wallet UI checkout, live college settlement, hosting, or unused sponsors without evidence | Technical; documentation corrections underway |
| P1 | Retain a judging build, source tag, instructions, availability and Sandbox/AI access through **December 15, 8:00 a.m. Pacific** | Obligations continue beyond the November deadline | Ongoing; plan needed |

## Section-by-section coverage

### Preamble and sections 1–2: entry, calendar and responsible parties

| Rule | Requirement or effect from supplied text | Solvia evidence / status | Risk and next step |
|---|---|---|---|
| Preamble | No purchase/payment is needed to enter or improve winning chances; submitting an entry constitutes an agreement for each entrant member | Sandbox test funds were used; no real payment is needed. Terms acceptance is **Participant** | Do not buy tools or process real money merely to qualify. This audit and draft editing do not constitute an assistant's confirmation of the entrant's legal agreement |
| §1 | Submission window: **Oct 1, 2026, 9:15 a.m. Pacific – Nov 12, 2026, noon Pacific** | **Ongoing**; draft exists before deadline | Finish all fields/artifacts and verify receipt before noon, not merely start uploading then |
| §1 | Judging: **Dec 1, 2:00 p.m. – Dec 15, 8:00 a.m. Pacific**. Announcement around **Dec 21, 2:00 p.m. Pacific** | **Ongoing** | Preserve free working access through the exact judging end. An expected announcement is not a promised award date |
| §2 | Sponsor is PayPal Inc., 2211 North First Street, San Jose, CA 95131; administrator is Devpost Inc., 250 Broadway, Floor 24, New York, NY 10007 | **Administrative context** | Organizer decisions and support requests belong to these responsible parties; project branding does not imply their endorsement |

### Section 3: entrant eligibility — participant facts, not software checks

| Rule | Requirement | Evidence / status | Risk and next step |
|---|---|---|---|
| §3 | An individual must have reached the legal age of majority where they reside at entry; teams consist of eligible individuals; organizations must already legally exist | **Participant**; form age checkbox not confirmed | Confirm every member's eligibility. General Devpost parental-consent provisions do not override this event's majority-age rule |
| §3 | Teams/organizations appoint an authorized eligible representative; entering on their behalf warrants that authority. An eligible individual may participate in multiple teams/organizations and individually | **Participant**; submitter type and representation not yet settled | Confirm who the entrant is, members and authority; do not choose an organization or team on assumption |
| §3 | Residence/domicile must permit entry and prizes under U.S. and local law. Named exclusions include Brazil, Quebec, Russia, Crimea, Cuba, Iran, North Korea and comprehensively OFAC-sanctioned places; the list is not exhaustive | **Participant**; country/jurisdiction checkbox unanswered | Confirm actual residence/domicile and applicable restrictions. A California public profile is not proof; Canada/province fields require the entrant's real answer or applicable N/A |
| §3 | Excludes promotion entities; their employees, representatives and agents; event-design/production/promotion/execution/distribution participants; judges and their employers; relevant parents, subsidiaries/affiliates; and apparent/actual conflicts | **Participant**; conflict checkbox unanswered | Check affiliations, contracts and judge-employer relationships for all members. Organizer has discretion over conflicts |
| §3 definitions | Family includes spouse, children/stepchildren, parents/stepparents, siblings/stepsiblings. Household includes another person sharing residence at least three months/year. Agents include directed contractual work for a promotion entity. Affiliates include control, common management or substantial ownership | **Participant** | Include these defined relationships in the exclusion review, not only the entrant's own employment |

### Section 4: technical and submission requirements

| ID / rule | Requirement | Solvia evidence / status | Remaining risk and action |
|---|---|---|---|
| T01 §4 entry | Have a Devpost account, register for this event, access the tools under their licenses, and complete required submission fields during the window | Existing registered project/draft is **Verified**; full submission is **Missing** | Finish the actual event form and verify final receipt; a portfolio URL alone is insufficient |
| T02 §4 build | Build/update an application with PayPal developer-platform **Sandbox** integration plus an AI tool/model/platform; PayPal must be central | Sandbox integration **Verified**: 20-check matrix plus two actual-provider negative checks; 14 successful matrix captures, $945 gross test funds; AI adapter present but live inference **Partial** | Demonstrate actual AI use. Do not substitute Codex-assisted development, configured code, a fallback, or fixture responses for unqualified runtime-model claims |
| T03 §4 tools | Any AI tool is allowed; named sponsor tools are optional for the main entry. Sponsor prizes have §8 conditions | AG Grid Community runs in fee comparison and the AG Grid selection/use explanation were saved and read back; **Verified limited use** | Keep AG Grid claims accurate. Do not add unused products simply to fill sponsor boxes |
| T04 §4 functionality | Install and run consistently on the stated intended platform, with behavior matching video/text | .NET MVC build, tests, Windows localhost, previous clean-install/free-path record; **Partial current full flow** | Repeat a fresh install after final changes with real AI and payment access. State laptop/browser prerequisites and optional paths precisely |
| T05 §4 new/existing | New work is allowed; pre-window work needs significant updates during the window and an explanation; organizer decides significance | **Participant-confirmed New, saved/read-back verified**: the entrant confirmed first building Solvia during this hackathon and its independent development; Additional Info records New and prior-work N/A | Keep the independent hackathon-build description consistent. This provenance statement does not establish every ownership or eligibility fact |
| T06 §4 third-party | APIs, SDKs and data must be authorized under applicable terms/licenses | MIT project LICENSE, NOTICES, bundled Apache-2.0 Roboto license/provenance and package references; **Partial rights audit** | Confirm provenance/terms for code, models, fonts, logos, screenshots, fee data and any music. Public availability alone is not a license or campus authorization |
| T07 §4 description | Explain actual project features/functionality | Updated English story saved; **Verified artifact** | Keep claim parity after edits. Distinguish demo employee exemption/public pricing from actual campus policy and fictional permits from real parking authorization |
| T08 §4 demo access | Provide a functional working build via complete repository setup/run instructions **or** hosted demo. Static mockups do not qualify | README and working local build; repository setup-anchor URL and judge instructions saved/read back; no hosted deployment claimed; **Partial full-path verification** | Hosting is not a rule requirement. Validate full setup with actual AI and Sandbox access; a screenshot or localhost URL alone cannot supply remote interaction |
| T09 §4 repository | Supply a public GitHub/Bitbucket/GitLab source URL with all source, assets and instructions; open-source LICENSE must be visible/detectable | Public GitHub metadata and remote MIT LICENSE independently verified; **Verified limited item** | Recheck default-branch contents/license presentation after push. Ensure downloadable assets/instructions suffice and secrets/private database files stay out |
| T10 §4 video | Video should be under 3 minutes, show the project operating on its intended device, and be uploaded and **publicly visible on YouTube**, with submitted URL | No video URL; **Missing** | Publish the actual recording. The editor's “public or unlisted” allowance is broader than the formal rule; use **public**. Do not fabricate a URL or show mocked payments as real |
| T11 §4 video rights | No third-party trademarks, copyrighted music or other material without permission | App uses PayPal/AG Grid and owned Solvia branding; no completed video rights inventory; **Participant/Partial** | Retain permitted branding/use evidence and use original/licensed media. Remove unrelated browser tabs, personal data and unlicensed music from recording |
| T12 §4 multiple entries | Each separate entry must be unique and substantially different from the entrant's other submissions, at organizer discretion | One current Solvia event entry identified; **Conditional** | If entering another project, compare scope and disclose overlap rather than duplicate the same app |
| T13 §4 ownership | Original work, entrant ownership and no infringement of IP, contract, privacy or other rights; assistance is allowed under stated ownership/originality conditions | Entrant describes Solvia as an independent new hackathon build; MIT notices retained; **Participant rights confirmation still needed** | Confirm employer/client/coauthor rights and asset provenance. An MIT file is not proof the entrant owns every submitted component |
| T14 §4 open source | Third-party open source may be used with license compliance and meaningful work enhancing/building upon it | Solvia adds application flows over .NET/EF/PDFsharp/AG Grid; **Partial** | Retain required notices and actual enhancements; do not claim ownership of upstream components |
| T15 §4 testing | Provide free, unrestricted judging/test access until judging ends; if private, give login instructions/credentials and any special access steps. Judges may rely only on text/images/video | Fictional personas, free Sandbox and saved/read-back judge instructions explaining local build, private credentials and AI setup; **Documentation verified / execution and availability ongoing** | Validate the complete clean judge route with AI/payment access through Dec 15 8 a.m. Pacific. Maintain availability and any necessary access without committing secrets |
| T16 §4 unusual hardware | Sponsor may require physical access when proprietary/non-common hardware is essential | Ordinary browser/computer; **Not currently applicable** | Reassess only if specialized hardware is introduced |
| T17 §4 language | English materials or English translations of all submitted text, testing instructions and demo | README/story/tests documentation are English; video absent; **Partial** | Record English narration/captions and inspect all final submitted material |
| T18 §4 preferential support | Project or derivative must not have received disallowed financial/preferential sponsor/administrator support; examples include funding/investment, commissioned development or a commercial license before the window ends | Funding/employment/contracts history not established; **Participant** | Confirm facts. Distinguish permitted event-provided free developer tools from potentially disqualifying special development support; ask organizer if ambiguous |

### Sections 5–6: submission version and judging

| Rule | Requirement or decision framework | Solvia evidence / status | Next step |
|---|---|---|---|
| §5 drafts | Drafts may be saved before deadline; a portfolio can continue changing afterward, but the event submission freezes | Draft saved; **Verified** | Save a final source tag and copy of submitted fields/video/screenshots. Do not assume later portfolio edits update the judged entry |
| §5 exceptions | After deadline, only organizer-permitted changes for potentially infringing, personal or inappropriate material; substance must remain the same | **Conditional** | Request permission for any such correction; no assumed right to feature fixes after deadline |
| §6 methods | Organizer selects methodology/panels and may use expert review, peers, automated AI analysis or combinations; judges/panels may change; viability/theme/API fit is pass/fail first | **Organizer decision** | Make the real PayPal-plus-AI path easy to verify; no assumption that one named judge, popularity or test count determines eligibility |
| §6 scoring | Five equal criteria: implementation, design, potential impact, innovation/idea, presentation (**20% each**, arithmetic from equal weighting) | **Readiness evidence, not official score** | Real AI + reliable payments; coherent mobile flow; credible named audience; differentiation; clear end-to-end demo. No numeric judge score is knowable now |
| §6 ties | Compare tied entries by criteria in listed order, starting implementation, then next criteria, then judge vote if all remain tied | **Organizer decision** | Technical substance matters in a tie, while every criterion still has equal ordinary weight |

### Section 7 and legal portions of sections 4, 9–15: entrant commitments

These are separate from software acceptance tests. Listing them does not check the entrant's boxes or accept agreements on anyone's behalf.

| Rule | Legal effect / obligation from supplied text | Status and concrete action |
|---|---|---|
| §7 ownership/judging | Entrants retain project IP and grant a non-exclusive judging-use license | **Participant**: confirm ability to grant rights; preserve third-party permissions/credits |
| §7 promotion | Sponsor/Devpost may promote the entry and contributor name, likeness, voice and image during the event and three years afterward; some components may be public and others seen for evaluation | **Participant**: ensure contributors understand publicity and remove confidential/personal material. This three-year wording is not a universal limit on every incorporated content license |
| §7 warranties | Ownership/permission for protected content and no harmful/malicious code are warranted on entry/prize acceptance | **Participant + technical**: provenance review and appropriate dependency/security review; tests alone are not complete assurance |
| §9 relationship/decisions | No confidential/fiduciary/special relationship; rules and organizer/judge decisions bind entrants | **Participant**: do not submit secrets or material requiring confidentiality |
| §9 release | Broad release, indemnity, defense and hold-harmless obligations cover participation, submission, prizes and permitted exploitation, including listed rights/injury/property claims and legal costs | **Participant**: review the actual terms before entry; not a build requirement and not something the assistant can certify away |
| §9 failures/receipt | Organizer disclaims responsibility for inaccurate information, technical failures, intervention/errors, injury/damage, incomplete/late/lost submissions or changed contact details. Sending is not receipt. Promptly request resubmission if lost/corrupt; permission is discretionary | **Ongoing**: keep backups, current contact information and verified final submission receipt; allow time for failures |
| §10 publicity | Worldwide promotional display/use of entry and personal details such as name, likeness, photo, voice, opinions, comments and hometown/country, without further pay/review except where law prohibits | **Participant**: review publicity exposure and media rights; a feedback-contact No does not revoke the event publicity terms |
| §11.1–3 administration/conduct | Event may be canceled/suspended/modified for unforeseen factors; disqualification for actual/apparent tampering, rule/law violations or inappropriate/unsporting conduct; undermining conduct can trigger investigation and civil/criminal referral | **Ongoing**: use honest entries, no manipulation, cooperate with legitimate requests; no assumption of a guaranteed competition/prize outcome |
| §11.4–6 hierarchy/changes | Formal rules override inconsistent form/site/advertising statements; amendments take effect as posted/specified. A participant believing a rule ambiguous before deadline must request written clarification | **Ongoing**: recheck the live rules before final entry. The form now saves a truthful setup-instructions URL; seek written clarification if its acceptance remains unclear. No invented hosting or silent weakening of the public-video rule |
| §11.7 severability | Non-enforcement is not waiver; an invalid term does not void the rest and may be replaced by the closest valid provision | **Participant context**: do not infer exemption because another entrant omitted something |
| §11.8 organizer IP | Event/organizer assets remain reserved; unauthorized copying/use is prohibited; submission use only within permission/rules | **Participant + technical**: use allowed integration branding, not copied event artwork or competitor designs as owned Solvia assets |
| §12 liability | Participation/prize liability releases apply, with local-law exceptions for prohibited limits such as gross negligence, intentional acts, death/bodily injury | **Participant**: review applicability; this audit does not resolve jurisdiction-specific enforceability |
| §13 disputes | Except where prohibited: individual AAA binding arbitration near contestant; FAA governs arbitration; award enforceable in a competent court; no class claims; damages limited to actual entry expenses, with waived punitive/incidental/consequential/multiplied damages and fees as stated; New York substantive law; local exceptions preserved | **Participant**: read the legal provisions, especially before committing. Generic Devpost forum/arbitration language does not override this event where inconsistent |
| §14 incorporated terms | Devpost Terms apply; “Poster” means Sponsor, and official rules control event conflicts | **Participant/Ongoing**: see the concise incorporated-terms review below |
| §15 privacy/support | Entrant information falls under Devpost Privacy; questions directed to `support@devpost.com` | **Participant/Ongoing**: review what is shared publicly/with organizers; any support message must come through an authorized human action, not an unrequested automatic send |

### Section 8: complete prize and sponsor-condition review

The supplied table and live table were read in full. Each sponsor row specifies an otherwise eligible entry using **any tools from that named sponsor**. No extra product-tier, SDK-version, agent-count, hosted-provider or paid-subscription qualification was found in this table. That is the published eligibility wording, not a guarantee of selection or a substitute for each tool's own license.

| Prize | Value | Number | Published eligibility | Solvia position |
|---|---|---:|---|---|
| Grand 1st / 2nd / 3rd | $12,000 / $8,000 / $5,000 USD | 1 each | Eligible entries | Eligible only after all applicable requirements and entrant facts are satisfied; no award predicted |
| Most Creative; Most Impactful; Best Demo Delivery; Best Use of PayPal + AI; Best Use of Agentic Commerce | $5,000 USD each | 1 each | Eligible entries | Categories, not extra automatic qualifications; do not describe ordinary assisted checkout as proven autonomous/agentic commerce |
| AG Grid 1st | $5,000 USD | 1 | Uses any AG Grid tools | Actual AG Grid **Community** fee comparison is evidenced; organizer judges its quality |
| AG Grid 2nd | $2,000 USD | 1 | Uses any AG Grid tools | Same |
| AG Grid 3rd | $1,000 USD | 3 | Uses any AG Grid tools | Same |
| APIMatic | $1,000 USD + 6 months Business subscription | 3 | Uses any APIMatic tools | Context document is not proof of running APIMatic; do not claim this prize without actual tool-use evidence |
| Bryntum | $1,000 USD | 3 | Uses any Bryntum tools | Not used; native term list is not Bryntum |
| Channel3 | $1,500 USD | 1 | Uses any Channel3 tools | Not evidenced; do not select as used |
| Render 1st / 2nd / 3rd | $1,000 / $750 / $500 **Render credits** | 1 each | Uses any Render tools | Planned deployment is not actual use; credits are not cash |

Section 4 also names Astropods, Elastic, KERNEL, Postman and Zapier as possible tools; **section 8 lists no separate prize for those names**. Do not infer a prize from being a named sponsor or from an artifact using its name.

| Remaining §8 clause | Effect | Action/status |
|---|---|---|
| Multiple-prize cap | At most **one Grand + one Sponsor**, **or one Honorable Mention + one Sponsor**; the text does not allow stacking Grand plus Honorable Mention | **Participant planning**: no promise of multiple main awards |
| Substitutions/non-award | Winner cannot transfer prizes; sponsor may substitute equal/higher value and may make no award without eligible entries | **Organizer discretion** |
| Winner verification | Identity, qualifications and creation role must be verified; an announcement is not final winner status before affidavits/forms and organizer determination | **Conditional**: keep truthful identity/contribution records and respond if selected |
| Payment/representative | Individual receives their prize; team representative or organization receives as specified; representative allocates among members | **Participant**: settle team representation/allocation; no assistant assumption about splits |
| Forms/delivery | Return correct required forms within **10 business days after sent**; incorrect/missing information can delay, disqualify or forfeit. Delivery within **60 days of receipt of completed forms**, not necessarily 60 days after announcement | **Conditional**: monitor contact channel, calendar actual form dates, provide correct information privately |
| Fees/taxes/banking | Winners/members bear receipt/use fees, currency/wiring costs, applicable tax reporting/payment, withholding forms (possible W-9/W-8BEN), foreign-exchange/banking rules and required government reports; provider may withhold taxes | **Conditional/Participant**: handle with actual residence/entity facts; Sandbox test funds are not prize income |

## Incorporated Devpost terms and privacy — additional material points

The [Terms](https://info.devpost.com/legal/terms-of-service), sections 1–20, were read. Practical additions: protect account credentials; supply accurate identity, affiliations and contribution credits; disclose no unauthorized confidential/personal material; avoid infringement, malware, duplicate submissions, spam, manipulation and automated scraping. Keep backups; a public listing is not eligibility. Respond to verification/access requests. Entrants retain ownership, but software/submissions receive a **perpetual worldwide promotional-display/use license**; general content has broader licensing. Do not treat the rules' contributor-publicity period as limiting every content license. Content/account removal, liability/indemnity and dispute provisions also apply; official event rules prevail in conflicts. Organizer-only hosting duties are not additional Solvia entrant requirements. No public-voting prize appears in the supplied event rules; generic vote rules do not create one. **Status: Participant/Ongoing**; no acceptance or legal clearance is recorded by this audit.

The [Privacy Policy](https://info.devpost.com/legal/privacy-policy) was read in full. It covers contact/profile, registration/submission and usage/cookie data; profiles are public, and registration/submission information can be shared with organizers. Service/marketing/analytics partners, affiliates, lawful disclosures and business transfers are covered. Marketing preferences can be changed; transactional email remains. Submitted personal data may be locked during the competition. Data is operated/transferred in the U.S.; retention and limited removed-content archives are described. External sites have their own policies. Security is not absolute. **Status: Participant**: minimize personal information in public artifacts and choose optional feedback contact separately.

Conditional privacy rights: the [California notice](https://info.devpost.com/legal/california-consumer-privacy-act) describes access/deletion and related verifiable-request choices for California residents; the [GDPR notice](https://info.devpost.com/legal/gdpr-privacy-notice) describes EEA rights and transfer safeguards. Actual applicability depends on residence/location; the audit does not infer it from the profile or select form answers.

## Actual Devpost form: requirements beyond the story editor

Initial inspection found all 22 answers blank. A subsequent authorized save was read back from the actual form: fields **4, 5, 6, 7, 8, 9, 10, 14, 15 and 19** now contain the factual answers below. The three eligibility checkboxes **16, 17 and 18 remain `checked: false`**. No terms were accepted and no final submission occurred. Some visibly required selections lacked an HTML `required` flag; the visible question and server validation govern, and a missing DOM attribute is not permission to skip it.

This does not make every optional question mandatory. Optional sponsor-experience fields may stay blank when inapplicable; conditional fields need the truthful applicable answer or an accepted N/A.

| Field group | Responsible answer / status |
|---|---|
| Submitter type; residence country; Canada province/N/A; organization/N/A | **Participant**: answer actual facts, not profile guesses |
| New/existing classification; prior work | **Saved and read-back verified**: New; N/A because Solvia is independent and first built during this hackathon, as the participant confirmed |
| Technology implementation; sponsor tools; repository URL | **Saved and read-back verified**: PayPal payment roles and 20+2-check evidence; configured Phi-4-mini/Ollama with live-model gap disclosed; AG Grid selected; `https://github.com/jlhpacheco/solvia`; no unverified sponsor selections |
| Working-demo URL; testing instructions | **Saved and read-back verified documentation**: `https://github.com/jlhpacheco/solvia#run-it-locally`, expressly identified as local setup instructions, plus .NET, fictional personas, private Sandbox configuration, separate buyer/test-card and Ollama steps. No hosted endpoint is claimed. The form saved this URL; clean full-path execution and organizer assessment remain separate |
| Developer-experience rating (1–10) and explanation | **Participant opinion**: the assistant must not invent a rating or personal history |
| Feedback-contact Yes/No | **Participant preference**: optional marketing/research contact choice, not generic consent to all privacy/publicity terms |
| APIMatic experience; AG Grid experience | **Saved and read-back verified**: APIMatic N/A/no execution claimed; actual AG Grid Community search, sorting, CSV data and native fallback described; AG Charts/Studio not claimed |
| Three eligibility affirmations | **Participant**: age, exclusions/conflicts/jurisdiction must be true before checking; presently unconfirmed |
| AI tools used | **Saved and read-back verified**: Codex-assisted development disclosed separately from the configured Phi-4-mini/Ollama runtime adapter; human review/fallback and pending live-model evidence stated |
| Learning level; career value of AI | **Participant opinion**: no guessed personal answers |

The listed factual fields are now saved; personal answers and eligibility affirmations remain unfinished. The project still showed **Draft, 2 of 5 steps** after this work. Saved fields do not establish final event submission; inspect an actual confirmation/receipt after the remaining steps.

## Evidence limits and discrepancies to close

| Finding | Evidence / consequence | Resolution status |
|---|---|---|
| Actual payment proof is strong but scoped | 20 matrix checks: 16 student/public applications across four colleges, 14 provider-confirmed captures totaling $945 gross Sandbox funds, two free cases, employee/missing-price guards, and 16 validated PDFs. Two additional actual-provider negative checks passed: unapproved order and refused card. They added no successful funds or issued permits. Provider lookups found one capture per successful order; repeat create/capture reused references | **Verified** test-card/REST path; not proof of Personal-wallet UI, live-money processing, refunds, delivered real webhooks or separate campus merchant settlement |
| Configured AI is not live proof | `OllamaExtractionService` calls the configured endpoint; fallback is labeled. No actual model result was verified | **Open**; record real `ollama` response before presenting live AI as complete |
| Clean-install log is historical/limited | Earlier pristine run built and completed free/fallback permit; later fixes/payment matrix ran on the configured preview | **Partial**; clean judge run should include the final code, model and Sandbox access |
| Privacy claim drift | At audit discovery `docs/USE-CASE.md` and historical plan text still said processing stayed on the student's computer, while README/PRIVACY correctly identified the app host | **Resolved in current documentation after read-back**: app-host/configured-endpoint wording replaced the absolute claims. Deployment/endpoint retention guarantees must not return |
| Sponsor claims can exceed evidence | Actual AG Grid is demonstrated; Elastic provider, workflow artifacts and Render plan are not equivalent to live integrations | **Resolved in current README and saved form after read-back**: AG Grid is the demonstrated sponsor; supporting artifacts/plans and unverified execution are explicitly separated |
| Third-party rights are not fully established by notices | LICENSE/NOTICES/font license/provenance are present; fee CSV cites public PMB pages, and Solvia uses campus names and payment branding | **Participant/Partial**; preserve license notices, source attribution and rights evidence. CSV is a research/demo catalog, not a campus contract, policy certification or live feed |
| Historical baseline and independence have direct participant answers | The entrant confirmed Solvia is independent and first built during this hackathon; Git independently records October 19 Solvia work | **Resolved participant chronology; New/N/A saved and read-back verified**. IP and eligibility remain separate |
| Publication/submission is unfinished | Draft story/images exist, public video absent, form answers and eligibility incomplete | **Open**; finish and read back actual saved fields/receipt before calling the entry submitted |

## Suggested written clarification if the form remains ambiguous

The form has now saved the truthful repository setup URL and explicit local-build instructions. That is not a hosted deployment or a guarantee of organizer acceptance. If written clarification is still desired, the following is prepared for the participant to review and send; **no message has been sent**:

> Section 4 permits a functional build through complete repository setup/run instructions instead of public hosting. Our Solvia build is runnable from the public repository with documented Sandbox and AI setup, but Additional Info requires a working-demo URL. May we put the repository's judge/setup-instructions URL in that field and describe the local test build, or is a different link required? We will provide the separate public YouTube demo required by the rules.

## Final readiness decision

**Do not mark the entry compliant, final, or submitted yet.** Close the technical artifacts, obtain accurate participant answers and rights confirmations, finish the remaining form, verify the final receipt, and maintain the judging build. Recheck the live rules before submission and again if the organizer posts an amendment. Even a complete audit cannot guarantee eligibility, legal clearance, judging scores or an award.

## Independent second-review reconciliation

A second reviewer first wrote an independent review of all 15 supplied sections, the live rules, and incorporated references, then read this audit. **No material rule-section or participant-obligation omission was found.** The second review independently caught the exact judging end, support/conflict restrictions, prize stacking, forms deadline, perpetual submission-promotion license versus contributor publicity, public-video rule, form/access distinction and entrant confirmations.

Its two evidence refreshes are incorporated here. First, payment coverage is **20 matrix checks plus two separate provider-negative checks**, not 22 successful payments: an unapproved order returned HTTP 409/`ORDER_NOT_APPROVED`; a refused official Sandbox card produced a `DECLINED` capture with processor code `0500`. Both applications stayed unpaid, with no live verification link, and PDF issuance returned HTTP 400. The 14 successful matrix captures still total $945. Second, Solvia's independent **New** classification rests on the participant's direct clarification and remains separate from other rights/eligibility facts.

The second review does not establish interactive wallet checkout or live AI. The subsequent factual form saves, New/N/A, current privacy wording and sponsor-claim corrections have now been read back. Final build verification, remaining personal fields and the submission receipt remain separate. No final submission, legal checkbox, support email or acceptance of terms was performed by either audit.

# Printable student and employee permits

Solvia's PDF generator is **PDFsharp 6.2.3, MIT License**. Embedded Roboto
Regular/Bold are **Apache-2.0**. Full font license and pinned provenance are
in `src/Solvia/Assets/Fonts/`; component notices are in `NOTICES.md`.
QuestPDF is no longer a runtime dependency. Test text extraction uses
Apache-2.0 PdfPig and needs no external `pdftotext` executable.

## How to print

1. Choose a fictional student or Elena (the employee) on Home.
2. Create a permit. Paid students need a completed PayPal Sandbox capture;
   published-free permits and the demo employee exemption issue without payment.
3. Open **My permits -> Permit (PDF)**, or the receipt's download link.
4. Open the downloaded PDF and print on **US Letter**, actual size (100%).

The single-page document contains permit number, large plate/state, valid
dates, campus, category, vehicle, payment standing, transaction IDs when
applicable, and a public verification URL. Employee permits identify the
demo exemption. Unpaid, failed, refunded, and expired payment states cannot
download a permit through the PDF endpoint. Owner checks apply to downloads.

Every generated permit visibly states that it is a sandbox demo and does
not authorize real campus parking. A printed URL works only while the
corresponding app host is reachable; `localhost` links work on that computer.

## Payment references

The illustrative student layout uses YCCD's published $0 term fee. It is
not evidence of a PayPal transaction: free and exempt permits need no order
or capture ID. A paid Fresno term permit instead requires the $30 catalog
fee to be captured. Checkout shows its order ID while approval is pending;
the receipt and PDF retain the actual capture ID after payment verification.
An order ID alone never proves payment. Missing references on a paid legacy
record are labeled unavailable, not "Not required".

## Maintenance

Fonts are embedded so generation is independent of installed OS fonts.
Long plates are reduced to fit their allocated width. Preserve third-party
notices when distributing the app. Retest the one-page layout and printable
margins after changing fields or typography.

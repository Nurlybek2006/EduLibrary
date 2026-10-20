# Original EduLibrary digital demo content

All five Kazakh texts were newly written for this demonstration, authored as **EduLibrary Demo Content**. They are not commercial textbook copies. Each has five lessons, explanations, code or process examples, exercises, answers, and a small project. Each PDF has eight pages; its EPUB contains the same material with a working table of contents.

Content license: **Creative Commons Attribution 4.0 International**, https://creativecommons.org/licenses/by/4.0/. Rights holder: EduLibrary Demo Content. The actual provenance page is `/digital-demo/provenance.html`. No full books from external sources were downloaded.

`content.json` is the original source. `manifest.json` records the book metadata and SHA-256 of each PDF/EPUB. `tools/generate_digital_demo.py` reproduces the files using ReportLab, pypdf and PyMuPDF with openly licensed Noto Sans fonts. Cover PNGs are generated from the original PDF title pages. No scripts or external resources are included in the EPUB packages.

`DigitalDemoSeeder.SeedAsync(IServiceProvider, CancellationToken)` registers five separate catalog books and ten actual assets through the same protected storage validators as staff uploads. A SQL transaction and completion marker prevent duplicate seeding. It reuses or creates only the demo author/publisher and needed category/subject links; no files are attached to existing commercial books and no physical copies or loans are created.

Reader dependencies are self-hosted in `wwwroot/lib`: PDF.js **6.4.299** (Apache-2.0), EPUB.js **0.3.93** (BSD-2-Clause), JSZip **3.10.1** (MIT/GPL dual license; MIT use). Their license files are retained alongside the distributions. PDF.js scripts are disabled and the evaluator is disabled. EPUB files are reconstructed by the server; EPUB.js frames have `sandbox="allow-same-origin"` without `allow-scripts` or popups.

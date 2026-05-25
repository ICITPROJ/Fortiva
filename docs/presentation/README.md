# Fortiva Technical Overview — Presentation

## Files

| File | Purpose |
|------|---------|
| `Fortiva-Technical-Overview.pptx` | **Main deliverable** — 32-slide deck with speaker notes |
| `images/` | Generated diagram assets used in slides |
| `build_fortiva_presentation.py` | Rebuild script (requires `python-pptx`) |

## Open the presentation

Double-click:

```
docs/presentation/Fortiva-Technical-Overview.pptx
```

Or from PowerPoint: **File → Open** and browse to the path above.

## Rebuild after code/docs changes

```powershell
pip install python-pptx
python docs/presentation/build_fortiva_presentation.py
```

## Slide outline (32 slides)

1. Title — Fortiva Technical Overview  
2. What Fortiva Is  
3. Solution Components  
4. Three Editions (+ diagram)  
5. **Section** — Vault Cryptography  
6. Zero-Knowledge Model (+ key hierarchy diagram)  
7. Argon2id parameters table (from source)  
8. Key hierarchy step-by-step  
9. AES-256-GCM / Windows CNG  
10. Vault file `.fva` layout (+ diagram)  
11. Atomic save + snapshots  
12. Rollback & Paranoia  
13. **Section** — Runtime & Data Locations  
14. On-disk paths table (Personal vs Enterprise)  
15. Unlock flow  
16. Windows Hello (explicit limitations)  
17. Browser bridge (+ diagram)  
18. **Section** — Enterprise Licensing & Policy  
19. License document fields (+ workflow diagram)  
20–23. LicenseTool: build, generate-key, sign, verify (command slides)  
24. Deploying license to clients  
25. Policy engine  
26. Shared vaults & seat enforcement  
27. Audit logging (HMAC JSONL)  
28. **Section** — Operations & QA  
29. Build & test commands  
30. Import / export / backup  
31. Threat model summary  
32. Questions / closing  

## Regenerate images only

Images are in `images/`. To replace them, edit prompts and regenerate, then rerun `build_fortiva_presentation.py`.

Content is sourced from: `README.md`, `docs/VAULT-FORMAT.md`, `docs/POLICY-LICENSING.md`, `docs/THREAT-MODEL.md`, `docs/UserManual.md`, and `src/Fortiva.LicenseTool/Program.cs`.

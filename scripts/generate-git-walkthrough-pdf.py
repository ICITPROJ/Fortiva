#!/usr/bin/env python3
"""Generate docs/GIT-PUSH-WALKTHROUGH.pdf from project git workflow content."""

from pathlib import Path

from fpdf import FPDF

ROOT = Path(__file__).resolve().parents[1]
OUT = ROOT / "docs" / "GIT-PUSH-WALKTHROUGH.pdf"


class WalkthroughPdf(FPDF):
    def header(self):
        self.set_font("Helvetica", "I", 9)
        self.set_text_color(100, 100, 100)
        self.cell(0, 8, "Fortiva - Push local changes to GitHub", align="R", new_x="LMARGIN", new_y="NEXT")
        self.ln(2)

    def footer(self):
        self.set_y(-15)
        self.set_font("Helvetica", "I", 8)
        self.set_text_color(120, 120, 120)
        self.cell(0, 10, f"Page {self.page_no()}", align="C")

    def h1(self, text: str):
        self.ln(4)
        self.set_font("Helvetica", "B", 18)
        self.set_text_color(20, 20, 20)
        self.multi_cell(0, 10, text)
        self.ln(2)

    def h2(self, text: str):
        self.ln(3)
        self.set_x(self.l_margin)
        self.set_font("Helvetica", "B", 13)
        self.set_text_color(30, 30, 30)
        self.multi_cell(0, 8, text)
        self.ln(1)

    def body(self, text: str):
        self.set_x(self.l_margin)
        self.set_font("Helvetica", "", 11)
        self.set_text_color(40, 40, 40)
        self.multi_cell(0, 6, text)
        self.ln(2)

    def code_block(self, text: str):
        self.set_x(self.l_margin)
        self.set_fill_color(245, 245, 245)
        self.set_font("Courier", "", 10)
        self.set_text_color(20, 20, 20)
        for line in text.strip().splitlines():
            self.cell(0, 6, "  " + line, fill=True, new_x="LMARGIN", new_y="NEXT")
        self.ln(3)

    def bullet(self, text: str):
        self.set_font("Helvetica", "", 11)
        self.set_text_color(40, 40, 40)
        self.set_x(self.l_margin)
        self.multi_cell(0, 6, f"  -  {text}")


def build() -> None:
    pdf = WalkthroughPdf()
    pdf.set_auto_page_break(auto=True, margin=20)
    pdf.add_page()

    pdf.h1("Push local changes to GitHub")
    pdf.body(
        "This guide covers the Fortiva desktop app repository (ICITPROJ/Fortiva). "
        "All commands run in PowerShell from your project folder."
    )

    pdf.h2("The workflow (overview)")
    pdf.body(
        "Edit code on your PC  ->  commit  ->  push to GitHub  ->  (optional) tag  ->  CI builds release"
    )
    pdf.body("GitHub only receives what you commit and push. The installed app updates only after a successful GitHub Release (tag).")

    pdf.h2("Step 0 - Open the project folder")
    pdf.code_block("cd C:\\Repo\\Github\\Fortiva")

    pdf.h2("Step 1 - See what changed")
    pdf.code_block("git status")
    pdf.body("Optional - view diffs:")
    pdf.code_block("git diff")

    pdf.h2("Step 2 - Stage files to push")
    pdf.body("Stage everything:")
    pdf.code_block("git add -A")
    pdf.body("Or stage one file:")
    pdf.code_block("git add src\\Fortiva.AppHost\\Pages\\SettingsPage.xaml.cs")

    pdf.h2("Step 3 - Commit (snapshot on your PC)")
    pdf.code_block('git commit -m "Describe what you changed in one line."')
    pdf.body('Example:')
    pdf.code_block('git commit -m "Add one-click Connect browser for extension setup."')

    pdf.h2("Step 4 - Push to GitHub")
    pdf.code_block("git push origin main")
    pdf.body("Verify: https://github.com/ICITPROJ/Fortiva/commits/main")

    pdf.h2("Step 5 (optional) - Publish an app update")
    pdf.body(
        "Pushing main alone does NOT update installed Fortiva apps. To ship an update users can install via Check for updates:"
    )
    pdf.code_block(".\\scripts\\publish-release.ps1")
    pdf.body("That script pushes main, creates a version tag from Directory.Build.props, and triggers the Release workflow.")
    pdf.body("Watch the build: https://github.com/ICITPROJ/Fortiva/actions")
    pdf.body("When the workflow is green, users can use Settings -> Check for updates in the app.")

    pdf.h2("Quick copy-paste (normal day)")
    pdf.code_block(
        """cd C:\\Repo\\Github\\Fortiva
git status
git add -A
git commit -m "Your message here"
git push origin main"""
    )

    pdf.h2("Which repository?")
    pdf.bullet("App (this folder): github.com/ICITPROJ/Fortiva")
    pdf.bullet("Website (separate project): github.com/ICITPROJ/Fortiva-Website")
    pdf.body("Push Fortiva for the desktop app - not Fortiva-Website.")

    pdf.h2("What each stage affects")
    pdf.bullet("Uncommitted edits: your PC only - nothing on GitHub")
    pdf.bullet("Committed, not pushed: still your PC only")
    pdf.bullet("Pushed to main: GitHub repo updated; CI runs tests")
    pdf.bullet("Tag pushed (e.g. v1.0.5): Release workflow builds installers + update manifest")
    pdf.bullet("Check for updates in app: pulls from GitHub Releases, not from main")

    OUT.parent.mkdir(parents=True, exist_ok=True)
    pdf.output(str(OUT))
    print(f"Wrote {OUT}")


if __name__ == "__main__":
    build()

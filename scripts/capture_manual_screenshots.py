"""Capture public DAMS frontend screenshots for the user manual."""

from __future__ import annotations

from pathlib import Path

from playwright.sync_api import sync_playwright

ROOT = Path(__file__).resolve().parents[1]
OUT = ROOT / "docs" / "user-manual-screenshots"
BASE = "http://127.0.0.1:5173"

ROUTES = [
    ("01-home.png", "/", 1280, 900),
    ("02-projects.png", "/projects", 1280, 900),
    ("03-about.png", "/about", 1280, 900),
    ("04-contact.png", "/contact", 1280, 900),
]


def capture() -> None:
    OUT.mkdir(parents=True, exist_ok=True)
    with sync_playwright() as p:
        browser = p.chromium.launch(headless=True)
        context = browser.new_context(
            viewport={"width": 1280, "height": 900},
            color_scheme="dark",
        )
        page = context.new_page()
        for filename, route, width, height in ROUTES:
            page.set_viewport_size({"width": width, "height": height})
            page.goto(f"{BASE}{route}", wait_until="networkidle")
            page.wait_for_timeout(1200)
            page.screenshot(path=str(OUT / filename), full_page=True)
        page.goto(f"{BASE}/", wait_until="networkidle")
        page.get_by_role("button", name="Log in").click()
        page.wait_for_timeout(800)
        page.screenshot(path=str(OUT / "05-login-modal.png"), full_page=False)
        page.goto(f"{BASE}/", wait_until="networkidle")
        page.get_by_role("button", name="Sign Up").click()
        page.wait_for_timeout(800)
        page.screenshot(path=str(OUT / "06-signup-modal.png"), full_page=False)
        browser.close()


if __name__ == "__main__":
    capture()
